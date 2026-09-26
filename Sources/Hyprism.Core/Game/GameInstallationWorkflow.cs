// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Net;
using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game.Patching;
using Hyprism.Core.Game.Download;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Accounts;
using Hyprism.Core.Migrations;

namespace Hyprism.Core.Game;

/// <summary>
/// Orchestrates the complete game download, update, and launch workflow.
/// Acts as the primary coordinator between version checking, patching, and game launching
/// </summary>
/// <remarks>
/// This service was refactored from a ~1000 line monolithic class into a coordinator
/// that delegates to specialized services like IPatchManager and IGameLauncher
/// </remarks>
public class GameInstallationWorkflow : IGameInstallationWorkflow
{
    private const long MinValidPwrBytes = 1_048_576;

    private readonly IConfigStore _configStore;
    private readonly IInstanceRepository _instances;
    private readonly IGameVersionCatalog _versions;
    private readonly IRuntimeProvisioner _runtime;
    private readonly IButlerClient _butler;
    private readonly IFileDownloader _downloader;
    private readonly IProgressReporter _progress;
    private readonly IPatchManager _patchManager;
    private readonly IGameLauncher _gameLauncher;
    private readonly HttpClient _httpClient;
    private readonly string _downloadsCacheDirectory;

    private readonly Dictionary<string, CancellationTokenSource> _downloadOperations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _ctsLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallationWorkflow"/> class
    /// </summary>
    /// <param name="configStore">Service for accessing configuration</param>
    /// <param name="instances">Service for managing game instances</param>
    /// <param name="versions">Service for version checking</param>
    /// <param name="runtime">Service for launch prerequisites (JRE, VC++)</param>
    /// <param name="butler">Service for Butler patch tool</param>
    /// <param name="downloader">Service for file downloads</param>
    /// <param name="progress">Service for progress notifications</param>
    /// <param name="patchManager">Manager for differential updates</param>
    /// <param name="gameLauncher">Launcher for the game process</param>
    /// <param name="httpClient">HTTP client for network requests</param>
    /// <param name="appPath">Application path configuration</param>
    public GameInstallationWorkflow(
        IConfigStore configStore,
        IInstanceRepository instances,
        IGameVersionCatalog versions,
        IRuntimeProvisioner runtime,
        IButlerClient butler,
        IFileDownloader downloader,
        IProgressReporter progress,
        IPatchManager patchManager,
        IGameLauncher gameLauncher,
        HttpClient httpClient,
        AppPathConfiguration appPath)
    {
        _configStore = configStore;
        _instances = instances;
        _versions = versions;
        _runtime = runtime;
        _butler = butler;
        _downloader = downloader;
        _progress = progress;
        _patchManager = patchManager;
        _gameLauncher = gameLauncher;
        _httpClient = httpClient;
        GameDownloadCacheMigration.Migrate(appPath.AppDir);
        _downloadsCacheDirectory = LauncherCachePaths.GetGameDownloadsDirectory(appPath.AppDir);
    }

    private Config CurrentConfig => _configStore.Configuration;

    /// <inheritdoc/>
    public Task<DownloadProgress> DownloadAndLaunchAsync(
        AuthUriPresenter? authorizationUriPresenter = null)
        => DownloadAndLaunchCoreAsync(null, authorizationUriPresenter);

    /// <inheritdoc/>
    public Task<DownloadProgress> DownloadAndLaunchInstanceAsync(
        string instanceId,
        AuthUriPresenter? authorizationUriPresenter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return DownloadAndLaunchCoreAsync(instanceId, authorizationUriPresenter);
    }

    private async Task<DownloadProgress> DownloadAndLaunchCoreAsync(
        string? instanceId,
        AuthUriPresenter? authorizationUriPresenter)
    {
        var selectedInstance = string.IsNullOrWhiteSpace(instanceId)
            ? _instances.GetSelectedInstance()
            : _instances.FindInstanceById(instanceId);
        if (selectedInstance == null)
        {
            Logger.Error("Download", "No target instance is available for launch");
            _progress.ReportError("fatal", "No target instance is available for launch");
            return new DownloadProgress { Error = "No target instance" };
        }

        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            if (_downloadOperations.ContainsKey(selectedInstance.Id))
            {
                Logger.Warning("Download", $"Operation already active for instance {selectedInstance.Id}");
                _progress.ReportError(
                    "download",
                    "An operation is already active for this instance",
                    instanceId: selectedInstance.Id);
                return new DownloadProgress { Error = "An operation is already active for this instance" };
            }

            cts = new CancellationTokenSource();
            _downloadOperations.Add(selectedInstance.Id, cts);
        }

        using var operation = _progress.BeginOperation(selectedInstance.Id);
        try
        {
            _progress.ReportDownloadProgress("preparing", 0, "launch.detail.preparing_session", null, 0, 0);

            var branch = LauncherUtilities.NormalizeVersionType(selectedInstance.Branch);
            var targetVersion = selectedInstance.Version;

            if (targetVersion <= 0)
            {
                const string error = "No explicit game version is selected for this instance";
                Logger.Error("Download", error);
                _progress.ReportError("download", "No game version selected", error, selectedInstance.Id);
                return new DownloadProgress { Error = error };
            }

            var versionPath = _instances.GetInstancePathById(selectedInstance.Id)
                ?? _instances.CreateInstanceDirectory(branch, selectedInstance.Id);

            Logger.Info("Download", $"Using instance path: {selectedInstance.Id} -> {versionPath}", false);

            Directory.CreateDirectory(versionPath);

            bool gameIsInstalled = _instances.IsClientPresent(versionPath);
            var instanceMeta = _instances.GetInstanceMeta(versionPath);
            var hasPendingUpdate = instanceMeta?.PendingVersion > 0;
            var installedVersionMatchesSelection = instanceMeta is null
                || instanceMeta.InstalledVersion <= 0
                || instanceMeta.InstalledVersion == targetVersion;

            if (gameIsInstalled && !hasPendingUpdate && installedVersionMatchesSelection)
            {
                Logger.Success("Download", $"Fast path: Game already installed at v{targetVersion}, skipping version check");
                return await HandleInstalledGameAsync(
                    versionPath,
                    branch,
                    selectedInstance.Id,
                    authorizationUriPresenter,
                    cts.Token);
            }

            _progress.ReportDownloadProgress("preparing", 1, "launch.detail.checking_versions", null, 0, 0);
            var versions = await _versions.GetVersionListAsync(branch, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            if (versions.Count == 0)
                return new DownloadProgress { Error = "No versions available for this branch" };

            if (!versions.Contains(targetVersion))
                return new DownloadProgress { Error = $"Selected version v{targetVersion} is not available for branch {branch}" };

            Logger.Info("Download", $"=== INSTALL CHECK ===", false);
            Logger.Info("Download", $"Version path: {versionPath}", false);
            Logger.Info("Download", $"Target version: {targetVersion}", false);
            Logger.Info("Download", $"Client exists (game installed): {gameIsInstalled}", false);

            if (instanceMeta != null && instanceMeta.PendingVersion > 0)
            {
                Logger.Warning("Download", $"Detected interrupted install: PendingVersion={instanceMeta.PendingVersion}, InstalledVersion={instanceMeta.InstalledVersion}");

                if (instanceMeta.PendingVersion != targetVersion)
                {
                    Logger.Warning("Download", $"Pending version v{instanceMeta.PendingVersion} does not match selected version v{targetVersion}; clearing stale pending state");
                    instanceMeta.PendingVersion = 0;
                    _instances.SaveInstanceMeta(versionPath, instanceMeta);
                }
                else if (gameIsInstalled && instanceMeta.InstalledVersion > 0 && instanceMeta.InstalledVersion < targetVersion)
                {
                    Logger.Info("Download", $"Resuming differential update from v{instanceMeta.InstalledVersion} to v{targetVersion}");
                    return await ApplyExplicitUpdateAsync(
                        versionPath,
                        branch,
                        instanceMeta.InstalledVersion,
                        targetVersion,
                        authorizationUriPresenter,
                        cts.Token);
                }
                else if (!gameIsInstalled)
                {
                    Logger.Info("Download", "Client not present despite PendingVersion, will re-install");
                }
                else
                {
                    Logger.Info("Download", $"Pending version v{targetVersion} is already present, clearing pending state");
                    instanceMeta.PendingVersion = 0;
                    _instances.SaveInstanceMeta(versionPath, instanceMeta);
                }
            }

            if (gameIsInstalled && instanceMeta is not null &&
                instanceMeta.InstalledVersion > 0 && instanceMeta.InstalledVersion != targetVersion)
            {
                if (instanceMeta.InstalledVersion < targetVersion)
                {
                    instanceMeta.PendingVersion = targetVersion;
                    _instances.SaveInstanceMeta(versionPath, instanceMeta);
                    return await ApplyExplicitUpdateAsync(
                        versionPath,
                        branch,
                        instanceMeta.InstalledVersion,
                        targetVersion,
                        authorizationUriPresenter,
                        cts.Token);
                }

                return new DownloadProgress
                {
                    Error = $"Installed version v{instanceMeta.InstalledVersion} is newer than selected version v{targetVersion}"
                };
            }

            if (!gameIsInstalled && instanceMeta != null)
            {
                instanceMeta.PendingVersion = targetVersion;
                _instances.SaveInstanceMeta(versionPath, instanceMeta);
            }

            if (gameIsInstalled)
            {
                return await HandleInstalledGameAsync(
                    versionPath,
                    branch,
                    selectedInstance.Id,
                    authorizationUriPresenter,
                    cts.Token);
            }

            return await HandleFreshInstallAsync(
                versionPath,
                branch,
                targetVersion,
                authorizationUriPresenter,
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Download", "Operation cancelled");
            return new DownloadProgress { Error = "Cancelled" };
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Fatal error: {ex.Message}");
            Logger.Error("Download", ex.ToString());
            _progress.ReportError("fatal", "Fatal error", ex.ToString());
            return new DownloadProgress { Error = $"Fatal error: {ex.Message}" };
        }
        finally
        {
            lock (_ctsLock)
            {
                if (_downloadOperations.TryGetValue(selectedInstance.Id, out var active) &&
                    ReferenceEquals(active, cts))
                {
                    _downloadOperations.Remove(selectedInstance.Id);
                }
            }
            cts.Dispose();
        }
    }

    /// <inheritdoc/>
    public void CancelDownload(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_ctsLock)
        {
            if (_downloadOperations.TryGetValue(instanceId, out var cts))
                cts.Cancel();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_ctsLock)
        {
            foreach (var cts in _downloadOperations.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _downloadOperations.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<DownloadProgress> HandleInstalledGameAsync(
        string versionPath,
        string branch,
        string instanceId,
        AuthUriPresenter? authorizationUriPresenter,
        CancellationToken ct)
    {
        Logger.Success("Download", "Game is already installed, skipping version check");

        await EnsureRuntimeDependenciesAsync(ct);

        _progress.ReportDownloadProgress("complete", 100, "launch.detail.launching_game", null, 0, 0);
        try
        {
            await _gameLauncher.LaunchGameAsync(versionPath, branch, authorizationUriPresenter, instanceId, ct);
            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Launch failed: {ex.Message}");
            _progress.ReportError("launch", "Failed to launch game", ex.ToString());
            return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
        }
    }

    private async Task<DownloadProgress> ApplyExplicitUpdateAsync(
        string versionPath,
        string branch,
        int installedVersion,
        int targetVersion,
        AuthUriPresenter? authorizationUriPresenter,
        CancellationToken ct)
    {
        try
        {
            await _patchManager.ApplyDifferentialUpdateAsync(
                versionPath,
                branch,
                installedVersion,
                targetVersion,
                ct);
            return await CompleteInstallAsync(
                versionPath,
                branch,
                targetVersion,
                authorizationUriPresenter,
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Differential update from v{installedVersion} to v{targetVersion} failed: {ex.Message}");
            return new DownloadProgress
            {
                Error = $"Failed to update game from v{installedVersion} to v{targetVersion}: {ex.Message}"
            };
        }
    }

    private async Task<DownloadProgress> HandleFreshInstallAsync(
        string versionPath,
        string branch,
        int targetVersion,
        AuthUriPresenter? authorizationUriPresenter,
        CancellationToken ct)
    {
        Logger.Info("Download", "Game not installed, starting download...");
        _progress.ReportDownloadProgress("download", 1, "launch.detail.preparing_download", null, 0, 0);

        try
        {
            _progress.ReportDownloadProgress("download", 2, "launch.detail.installing_butler", null, 0, 0);
            await _butler.EnsureButlerInstalledAsync((progress, message) =>
            {
                int mappedProgress = 2 + (int)(progress * 0.03);
                _progress.ReportDownloadProgress("download", mappedProgress, message, null, 0, 0);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Butler install failed: {ex.Message}");
            return new DownloadProgress { Error = $"Failed to install Butler: {ex.Message}" };
        }

        ct.ThrowIfCancellationRequested();

        bool officialDown = _versions.IsOfficialServerDown(branch);
        string osName = LauncherUtilities.GetOS();
        string arch = LauncherUtilities.GetArch();
        string apiVersionType = LauncherUtilities.NormalizeVersionType(branch);

        if (_versions.IsDiffBasedBranch(apiVersionType) &&
            (officialDown || string.IsNullOrWhiteSpace(_versions.GetVersionEntry(apiVersionType, targetVersion)?.PwrUrl)) &&
            await _versions.GetMirrorDownloadUrlAsync(osName, arch, apiVersionType, targetVersion, ct) is null)
        {
            Logger.Info("Download", $"No full mirror build for {apiVersionType} v{targetVersion}; installing via diff chain from v0");
            _progress.ReportDownloadProgress("download", 5, "launch.detail.downloading_mirror", null, 0, 0);

            try
            {
                await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, 0, targetVersion, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Mirror diff chain install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install game from mirror: {ex.Message}" };
            }

            return await CompleteInstallAsync(
                versionPath,
                branch,
                targetVersion,
                authorizationUriPresenter,
                ct);
        }
        else
        {
            string downloadUrl;
            CachedVersionEntry versionEntry;
            try
            {
                versionEntry = await _versions.RefreshAndGetVersionEntryAsync(apiVersionType, targetVersion, ct);
                downloadUrl = versionEntry.PwrUrl;
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Failed to get download URL: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to get download URL for v{targetVersion}: {ex.Message}" };
            }

            bool hasOfficialUrl = !string.IsNullOrEmpty(versionEntry.PwrUrl)
                && versionEntry.PwrUrl.Contains("game-patches.hytale.com")
                && versionEntry.PwrUrl.Contains("verify=");

            string pwrPath = Path.Combine(
                _downloadsCacheDirectory,
                $"{branch}_version_{targetVersion}.pwr");

            Directory.CreateDirectory(Path.GetDirectoryName(pwrPath)!);

            bool skipOfficial = officialDown || !hasOfficialUrl;

            try
            {
                await DownloadPwrWithCachingAsync(downloadUrl, pwrPath, osName, arch, apiVersionType, targetVersion, skipOfficial, hasOfficialUrl, ct);
            }
            catch (MirrorDiffRequiredException)
            {
                Logger.Info("Download", $"Switching to mirror diff chain for {apiVersionType} v{targetVersion}");
                _progress.ReportDownloadProgress("download", 5, "launch.detail.downloading_mirror", null, 0, 0);

                try
                {
                    await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, 0, targetVersion, ct);
                    return await CompleteInstallAsync(
                        versionPath,
                        branch,
                        targetVersion,
                        authorizationUriPresenter,
                        ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Error("Download", $"Mirror diff chain install failed: {ex.Message}");
                    return new DownloadProgress { Error = $"Failed to install game from mirror: {ex.Message}" };
                }
            }
            catch (MirrorBootstrapRequiredException ex)
            {
                if (!apiVersionType.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                {
                    return new DownloadProgress { Error = ex.Message };
                }

                Logger.Warning("Download", $"Mirror full pre-release v{targetVersion} unavailable ({ex.Message}). Trying previous full build + patch...");

                var installed = await TryInstallPreReleaseFromPreviousFullAsync(
                    versionPath,
                    branch,
                    apiVersionType,
                    osName,
                    arch,
                    targetVersion,
                    ct);

                if (!installed)
                {
                    return new DownloadProgress { Error = $"Failed to install pre-release v{targetVersion}: no valid base build + patch path found" };
                }

                return await CompleteInstallAsync(
                    versionPath,
                    branch,
                    targetVersion,
                    authorizationUriPresenter,
                    ct);
            }

            _progress.ReportDownloadProgress("install", 65, "launch.detail.installing_butler_pwr", null, 0, 0);

            try
            {
                await _butler.ApplyPwrAsync(pwrPath, versionPath, (progress, message) =>
                {
                    int mappedProgress = 65 + (int)(progress * 0.20);
                    _progress.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
                }, ct);

                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Download", $"PWR extraction failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install game: {ex.Message}" };
            }
        }

        return await CompleteInstallAsync(
            versionPath,
            branch,
            targetVersion,
            authorizationUriPresenter,
            ct);
    }

    private async Task<DownloadProgress> CompleteInstallAsync(
        string versionPath,
        string branch,
        int targetVersion,
        AuthUriPresenter? authorizationUriPresenter,
        CancellationToken ct)
    {
        var meta = _instances.GetInstanceMeta(versionPath);
        if (meta != null)
        {
            meta.InstalledVersion = targetVersion;
            meta.PendingVersion = 0;
            _instances.SaveInstanceMeta(versionPath, meta);
        }

        _progress.ReportDownloadProgress("complete", 95, "launch.detail.download_complete", null, 0, 0);

        await EnsureRuntimeDependenciesAsync(ct);

        ct.ThrowIfCancellationRequested();

        _progress.ReportDownloadProgress("complete", 100, "launch.detail.launching_game", null, 0, 0);

        try
        {
            var instanceId = _instances.GetInstanceMeta(versionPath)?.Id;
            await _gameLauncher.LaunchGameAsync(
                versionPath,
                branch,
                authorizationUriPresenter,
                string.IsNullOrWhiteSpace(instanceId) ? null : instanceId,
                ct);

            var cacheDir = _downloadsCacheDirectory;
            if (Directory.Exists(cacheDir))
            {
                foreach (var file in Directory.GetFiles(cacheDir, $"{branch}_*.pwr"))
                    try { File.Delete(file); } catch { }
            }

            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Launch failed: {ex.Message}");
            _progress.ReportError("launch", "Failed to launch game", ex.ToString());
            return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
        }
    }

    private async Task<bool> TryInstallPreReleaseFromPreviousFullAsync(
        string versionPath,
        string branch,
        string apiBranch,
        string os,
        string arch,
        int targetVersion,
        CancellationToken ct)
    {
        if (targetVersion <= 1)
            return false;

        for (int baseVersion = targetVersion - 1; baseVersion >= 1; baseVersion--)
        {
            var bootstrapPath = Path.Combine(
                _downloadsCacheDirectory,
                $"{branch}_bootstrap_{baseVersion}.pwr");
            Directory.CreateDirectory(Path.GetDirectoryName(bootstrapPath)!);

            try
            {
                Logger.Info("Download", $"Trying fallback base v{baseVersion} for target v{targetVersion}");

                await DownloadPwrWithCachingAsync(
                    downloadUrl: string.Empty,
                    pwrPath: bootstrapPath,
                    os: os,
                    arch: arch,
                    branch: apiBranch,
                    version: baseVersion,
                    skipOfficial: true,
                    hasOfficialUrl: false,
                    ct: ct);

                _progress.ReportDownloadProgress("install", 65, "launch.detail.installing_butler_pwr", null, 0, 0);

                await _butler.ApplyPwrAsync(bootstrapPath, versionPath, (progress, message) =>
                {
                    int mappedProgress = 65 + (int)(progress * 0.15);
                    _progress.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
                }, ct);

                await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, baseVersion, targetVersion, ct);
                Logger.Success("Download", $"Installed pre-release via fallback path: full v{baseVersion} + patches to v{targetVersion}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (MirrorBootstrapRequiredException ex)
            {
                Logger.Warning("Download", $"Fallback base v{baseVersion} invalid: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Warning("Download", $"Fallback path failed for base v{baseVersion}: {ex.Message}");
            }
        }

        return false;
    }

    private async Task DownloadPwrWithCachingAsync(
        string downloadUrl, string pwrPath,
        string os, string arch, string branch, int version,
        bool skipOfficial, bool hasOfficialUrl, CancellationToken ct)
    {
        bool needDownload = true;
        long remoteSize = -1;

        if (!skipOfficial && hasOfficialUrl)
        {
            try { remoteSize = await _downloader.GetFileSizeAsync(downloadUrl, ct); }
            catch { }
        }

        if (File.Exists(pwrPath))
        {
            if (remoteSize > 0)
            {
                long localSize = new FileInfo(pwrPath).Length;
                if (localSize == remoteSize && localSize >= MinValidPwrBytes)
                {
                    Logger.Info("Download", "Using cached PWR file.");
                    needDownload = false;
                }
                else
                {
                    Logger.Warning("Download", $"Cached file size mismatch ({localSize} vs {remoteSize}). Deleting.");
                    try { File.Delete(pwrPath); } catch { }
                }
            }
            else
            {
                long localSize = new FileInfo(pwrPath).Length;
                if (localSize >= MinValidPwrBytes)
                {
                    Logger.Info("Download", "Cannot verify remote size, using valid local cache entry.");
                    needDownload = false;
                }
                else
                {
                    Logger.Warning("Download", $"Cached PWR is too small ({localSize} bytes). Deleting and redownloading.");
                    try { File.Delete(pwrPath); } catch { }
                }
            }
        }

        if (needDownload)
        {
            string partPath = pwrPath + ".part";
            bool downloaded = false;

            if (!skipOfficial && hasOfficialUrl)
            {
                try
                {
                    Logger.Info("Download", $"Downloading from official: {downloadUrl}");
                    _progress.ReportDownloadProgress("download", 5, "launch.detail.downloading_official", null, 0, 0);
                    await _downloader.DownloadFileAsync(downloadUrl, partPath, (progress, downloaded, total) =>
                    {
                        int mappedProgress = 5 + (int)(progress * 0.60);
                        _progress.ReportDownloadProgress("download", mappedProgress, "launch.detail.downloading_official", [progress], downloaded, total);
                    }, ct);
                    downloaded = true;
                    Logger.Success("Download", "Downloaded from official successfully");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Warning("Download", $"Official download failed: {ex.Message}");
                    if (File.Exists(partPath)) try { File.Delete(partPath); } catch { }

                    if (IsHttpForbidden(ex))
                    {
                        try
                        {
                            Logger.Warning("Download", "Official URL returned 403. Forcing version cache refresh and retrying official download once...");
                            await _versions.ForceRefreshCacheAsync(branch, ct);

                            var refreshedEntry = _versions.GetVersionEntry(branch, version);
                            var refreshedOfficialUrl = refreshedEntry?.PwrUrl;
                            var hasRefreshedOfficialUrl =
                                !string.IsNullOrEmpty(refreshedOfficialUrl)
                                && refreshedOfficialUrl.Contains("game-patches.hytale.com")
                                && refreshedOfficialUrl.Contains("verify=");

                            if (hasRefreshedOfficialUrl)
                            {
                                Logger.Info("Download", $"Retrying official download after cache refresh: {refreshedOfficialUrl}");
                                _progress.ReportDownloadProgress("download", 5, "launch.detail.downloading_official", null, 0, 0);

                                await _downloader.DownloadFileAsync(refreshedOfficialUrl!, partPath, (progress, dl, total) =>
                                {
                                    int mappedProgress = 5 + (int)(progress * 0.60);
                                    _progress.ReportDownloadProgress("download", mappedProgress, "launch.detail.downloading_official", [progress], dl, total);
                                }, ct);

                                downloaded = true;
                                Logger.Success("Download", "Downloaded from official successfully after token refresh");
                            }
                            else
                            {
                                Logger.Warning("Download", "No refreshed official signed URL found after cache refresh");
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception refreshRetryEx)
                        {
                            Logger.Warning("Download", $"Official retry after cache refresh failed: {refreshRetryEx.Message}");
                            if (File.Exists(partPath)) try { File.Delete(partPath); } catch { }
                        }
                    }
                }
            }
            else if (!skipOfficial)
            {
                Logger.Info("Download", "No signed official URL available, skipping to mirror...");
            }
            else
            {
                Logger.Info("Download", "Official server is down, skipping to mirror...");
            }

            if (!downloaded)
            {
                var mirrorUrl = await _versions.GetMirrorDownloadUrlAsync(os, arch, branch, version, ct);
                if (mirrorUrl != null)
                {
                    try
                    {
                        var mirrorHeaders = await _versions.GetMirrorRequestHeadersAsync(mirrorUrl, ct);
                        try
                        {
                            var mirrorSize = await _downloader.GetFileSizeAsync(mirrorUrl, mirrorHeaders, ct);
                            if (mirrorSize >= 0 && mirrorSize < MinValidPwrBytes)
                            {
                                throw new MirrorBootstrapRequiredException(version, $"Mirror returned tiny full build ({mirrorSize} bytes) for v{version}");
                            }
                        }
                        catch (MirrorBootstrapRequiredException) { throw; }
                        catch { }

                        Logger.Info("Download", $"Retrying from mirror: {mirrorUrl}");
                        _progress.ReportDownloadProgress("download", 5, "launch.detail.downloading_mirror", null, 0, 0);

                        await _downloader.DownloadFileAsync(mirrorUrl, partPath, (progress, dl, total) =>
                        {
                            int mappedProgress = 5 + (int)(progress * 0.60);
                            _progress.ReportDownloadProgress("download", mappedProgress, "launch.detail.downloading_mirror", [progress], dl, total);
                        }, mirrorHeaders, ct);

                        long downloadedSize = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                        if (downloadedSize < MinValidPwrBytes)
                        {
                            throw new MirrorBootstrapRequiredException(version, $"Downloaded mirror full build is too small ({downloadedSize} bytes) for v{version}");
                        }

                        downloaded = true;
                        Logger.Success("Download", "Downloaded from mirror successfully");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (MirrorBootstrapRequiredException) { throw; }
                    catch (Exception mirrorEx)
                    {
                        Logger.Error("Download", $"Mirror download also failed: {mirrorEx.Message}");

                        if (IsHttpNotFound(mirrorEx))
                        {
                            Logger.Warning("Download", $"Version v{version} not found on mirror, invalidating cache entry");
                            _versions.InvalidateVersionFromCache(branch, version);
                        }
                    }
                }
            }

            if (!downloaded)
            {
                if (_versions.IsDiffBasedBranch(branch))
                {
                    Logger.Info("Download", $"No full mirror build available for {branch} v{version}; falling back to differential patches");
                    throw new MirrorDiffRequiredException(version);
                }

                throw new Exception("Download failed from both official server and mirror. Please try again later.");
            }

            if (File.Exists(partPath))
                File.Move(partPath, pwrPath, true);
        }
        else
        {
            _progress.ReportDownloadProgress("download", 65, "launch.detail.using_cached_installer", null, 0, 0);
        }
    }

    private static bool IsHttpForbidden(Exception ex)
    {
        if (ex is HttpRequestException hre && hre.StatusCode == HttpStatusCode.Forbidden)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("403 Forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpNotFound(Exception ex)
    {
        if (ex is HttpRequestException hre && hre.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase)
            || message.Contains("404 NotFound", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureRuntimeDependenciesAsync(CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _progress.ReportDownloadProgress("install", 94, "launch.detail.vc_redist", null, 0, 0);
            try
            {
                await _runtime.EnsureVCRedistInstalledAsync((progress, message) =>
                {
                    int mappedProgress = 94 + (int)(progress * 0.02);
                    _progress.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
            }
        }

        string jrePath = _runtime.GetJavaPath();
        if (!File.Exists(jrePath))
        {
            Logger.Info("Download", "JRE missing, installing...");
            _progress.ReportDownloadProgress("install", 96, "launch.detail.java_install", null, 0, 0);
            await _runtime.EnsureJREInstalledAsync((progress, message) =>
            {
                int mappedProgress = 96 + (int)(progress * 0.03);
                _progress.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
            }, ct);
        }
    }
}

/// <summary>
/// Thrown when a full download is unavailable and the mirror supports differential patches.
/// </summary>
internal class MirrorDiffRequiredException : Exception
{
    public int TargetVersion { get; }
    public MirrorDiffRequiredException(int targetVersion) : base("Mirror requires a differential download")
    {
        TargetVersion = targetVersion;
    }
}

internal class MirrorBootstrapRequiredException : Exception
{
    public int TargetVersion { get; }

    public MirrorBootstrapRequiredException(int targetVersion, string message) : base(message)
    {
        TargetVersion = targetVersion;
    }
}
