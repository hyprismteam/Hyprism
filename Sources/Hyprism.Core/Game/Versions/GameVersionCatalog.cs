// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Runtime.InteropServices;
using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game.Sources;

namespace Hyprism.Core.Game.Versions;

/// <summary>
/// Manages explicit game version discovery, patch planning, and version caching.
/// Queries all registered version sources (official + mirrors) and merges results
/// </summary>
/// <remarks>
/// Version information is cached to avoid excessive network requests.
/// Fetches from ALL sources and stores them separately, merging for queries.
/// Sources are queried by priority (official first, then mirrors)
/// </remarks>
public class GameVersionCatalog : IGameVersionCatalog
{
    private readonly string _appDir;
    private readonly IConfigStore _configStore;
    private readonly HttpClient _httpClient;
    private readonly IMirrorCatalog? _mirrorCatalog;
    private readonly List<IVersionSource> _sources;
    private readonly Lock _sourceSync = new();
    private readonly SemaphoreSlim _versionFetchLock = new(1, 1);

    private readonly HytaleVersionSource? _hytaleSource;
    private readonly List<IVersionSource> _mirrorSources = [];

    private volatile IVersionSource? _selectedMirror;

    /// <summary>
    /// In-memory cache of versions and patch data, backed by on-disk files
    /// </summary>
    private readonly VersionCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameVersionCatalog"/> class
    /// </summary>
    public GameVersionCatalog(
        string appDir,
        IConfigStore configStore,
        HttpClient httpClient,
        HytaleVersionSource? hytaleSource = null,
        IEnumerable<IVersionSource>? mirrorSources = null,
        IMirrorCatalog? mirrorCatalog = null)
    {
        _appDir = appDir;
        _configStore = configStore;
        _httpClient = httpClient;
        _mirrorCatalog = mirrorCatalog;

        _sources = [];

        if (hytaleSource != null)
        {
            _hytaleSource = hytaleSource;
            _sources.Add(hytaleSource);
        }

        mirrorSources ??= mirrorCatalog?.CreateEnabledSources();
        if (mirrorSources != null)
        {
            foreach (var mirror in mirrorSources.Where(m => m.Type == VersionSourceType.Mirror))
            {
                _mirrorSources.Add(mirror);
                _sources.Add(mirror);
            }
        }

        _sources.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        _cache = new VersionCache(appDir, GetMirrorSourceIdsSnapshot);

        foreach (var source in GetSourcesSnapshot())
        {
            Logger.Debug("Version", $"Source {source.SourceId}: full={source.LayoutInfo.FullBuildLocation}; patch={source.LayoutInfo.PatchLocation}; cache={source.LayoutInfo.CachePolicy}");
        }
    }

    /// <summary>
    /// Whether an official Hytale account is available for authenticated requests
    /// </summary>
    public bool HasOfficialAccount => _hytaleSource?.IsAvailable ?? false;

    /// <inheritdoc/>
    public async Task<List<int>> GetVersionListAsync(string branch, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        string osName = LauncherUtilities.GetOS();
        string arch = LauncherUtilities.GetArch();
        var maxAge = TimeSpan.FromMinutes(15);

        var cachedSnapshot = _cache.TryGet(osName, arch);
        if (cachedSnapshot != null && _cache.IsBranchFresh(cachedSnapshot, normalizedBranch, maxAge))
        {
            var versions = GetMergedVersionList(cachedSnapshot, normalizedBranch);
            if (versions.Count > 0)
            {
                Logger.Info("Version", $"Using cached versions for {branch}: [{string.Join(", ", versions)}]");
                return versions;
            }
        }

        // Prevent duplicate requests; the cache must be checked again after acquiring the lock
        await _versionFetchLock.WaitAsync(ct);
        try
        {
            cachedSnapshot = _cache.TryGet(osName, arch);
            if (cachedSnapshot != null && _cache.IsBranchFresh(cachedSnapshot, normalizedBranch, maxAge))
            {
                var versions = GetMergedVersionList(cachedSnapshot, normalizedBranch);
                if (versions.Count > 0)
                {
                    return versions;
                }
            }

            return await FetchVersionListCoreAsync(normalizedBranch, osName, arch, ct);
        }
        finally
        {
            _versionFetchLock.Release();
        }
    }

    private async Task<List<int>> FetchVersionListCoreAsync(string normalizedBranch, string osName, string arch, CancellationToken ct)
    {
        var snapshot = _cache.Load() ?? new VersionsCacheSnapshot
        {
            Os = osName,
            Arch = arch,
            FetchedAtUtc = DateTime.UtcNow,
            Data = new VersionsCacheData()
        };

        snapshot = _cache.Sanitize(snapshot);
        snapshot.SchemaVersion = VersionCache.CurrentSchemaVersion;
        snapshot.Os = osName;
        snapshot.Arch = arch;
        snapshot.FetchedAtUtc = DateTime.UtcNow;

        var patchSnapshot = _cache.LoadPatches() ?? new PatchesCacheSnapshot
        {
            Os = osName,
            Arch = arch,
            FetchedAtUtc = DateTime.UtcNow,
            Data = new PatchesCacheData()
        };
        patchSnapshot.Os = osName;
        patchSnapshot.Arch = arch;
        patchSnapshot.FetchedAtUtc = DateTime.UtcNow;

        foreach (var source in GetSourcesSnapshot())
        {
            if (source.Type == VersionSourceType.Official)
            {
                snapshot.Data.Hytale?.Branches.Remove(normalizedBranch);
                patchSnapshot.Data.Hytale?.Remove(normalizedBranch);
            }
            else
            {
                snapshot.Data.Mirrors.FirstOrDefault(m => m.MirrorId == source.SourceId)?
                    .Branches.Remove(normalizedBranch);
                patchSnapshot.Data.Mirrors.FirstOrDefault(m => m.MirrorId == source.SourceId)?
                    .Branches.Remove(normalizedBranch);
            }

            if (!source.IsAvailable)
            {
                Logger.Debug("Version", $"Source {source.SourceId} not available, skipping");
                continue;
            }

            Logger.Info("Version", $"Fetching from {source.SourceId} for {normalizedBranch}...");
            try
            {
                var versions = await source.GetVersionsAsync(osName, arch, normalizedBranch, ct);
                if (versions.Count > 0)
                {
                    if (source.Type == VersionSourceType.Official)
                    {
                        snapshot.Data.Hytale ??= new OfficialSourceCache();
                        snapshot.Data.Hytale.Branches[normalizedBranch] = versions;
                    }
                    else
                    {
                        var mirrorCache = snapshot.Data.Mirrors.FirstOrDefault(m => m.MirrorId == source.SourceId);
                        if (mirrorCache == null)
                        {
                            mirrorCache = new MirrorSourceCache { MirrorId = source.SourceId };
                            snapshot.Data.Mirrors.Add(mirrorCache);
                        }
                        mirrorCache.Branches[normalizedBranch] = versions;
                    }

                    Logger.Success("Version", $"{source.SourceId} returned {versions.Count} versions for {normalizedBranch}: [{string.Join(", ", versions.Select(v => v.Version))}]");
                }
                else
                {
                    Logger.Warning("Version", $"{source.SourceId} returned no versions for {normalizedBranch}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"{source.SourceId} fetch failed for {normalizedBranch}: {ex.Message}");
            }

            try
            {
                var patchSteps = await source.GetPatchChainAsync(osName, arch, normalizedBranch, ct);
                if (patchSteps.Count > 0)
                {
                    if (source.Type == VersionSourceType.Official)
                    {
                        patchSnapshot.Data.Hytale ??= [];
                        patchSnapshot.Data.Hytale[normalizedBranch] = patchSteps;
                    }
                    else
                    {
                        var mirrorPatch = patchSnapshot.Data.Mirrors.FirstOrDefault(m => m.MirrorId == source.SourceId);
                        if (mirrorPatch == null)
                        {
                            mirrorPatch = new MirrorPatchCache { MirrorId = source.SourceId };
                            patchSnapshot.Data.Mirrors.Add(mirrorPatch);
                        }
                        mirrorPatch.Branches[normalizedBranch] = patchSteps;
                    }

                    Logger.Success("Version", $"{source.SourceId} returned {patchSteps.Count} patch steps for {normalizedBranch}");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("Version", $"{source.SourceId} patch chain fetch failed for {normalizedBranch}: {ex.Message}");
            }
        }

        snapshot.BranchFetchedAt[normalizedBranch] = DateTime.UtcNow;

        _cache.Save(snapshot);
        _cache.SavePatches(patchSnapshot);
        _cache.Set(snapshot);

        var result = GetMergedVersionList(snapshot, normalizedBranch);
        Logger.Info("Version", $"Total versions for {normalizedBranch}: [{string.Join(", ", result)}]");
        return result;
    }

    /// <summary>
    /// Gets merged version list from cache, preferring official source for duplicates
    /// </summary>
    private static List<int> GetMergedVersionList(VersionsCacheSnapshot snapshot, string branch)
    {
        var allVersions = new Dictionary<int, VersionSource>();

        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(branch, out var mirrorVersions))
            {
                foreach (var v in mirrorVersions)
                {
                    allVersions[v.Version] = VersionSource.Mirror;
                }
            }
        }

        if (snapshot.Data.Hytale?.Branches.TryGetValue(branch, out var officialVersions) == true)
        {
            foreach (var v in officialVersions)
            {
                allVersions[v.Version] = VersionSource.Official;
            }
        }

        return [.. allVersions.Keys.OrderByDescending(v => v)];
    }

    /// <summary>
    /// Gets merged version entries from cache, preserving the display name when a source provides one
    /// </summary>
    private static List<CachedVersionEntry> GetMergedVersionEntries(
        VersionsCacheSnapshot snapshot,
        string branch)
    {
        var allVersions = new Dictionary<int, CachedVersionEntry>();

        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (!mirror.Branches.TryGetValue(branch, out var mirrorVersions))
                continue;

            foreach (var version in mirrorVersions)
            {
                if (!allVersions.TryGetValue(version.Version, out var existing) ||
                    string.IsNullOrWhiteSpace(existing.VersionName))
                {
                    allVersions[version.Version] = version;
                }
            }
        }

        if (snapshot.Data.Hytale?.Branches.TryGetValue(branch, out var officialVersions) == true)
        {
            foreach (var version in officialVersions)
            {
                if (string.IsNullOrWhiteSpace(version.VersionName) &&
                    allVersions.TryGetValue(version.Version, out var mirrorVersion) &&
                    !string.IsNullOrWhiteSpace(mirrorVersion.VersionName))
                {
                    version.VersionName = mirrorVersion.VersionName;
                }

                allVersions[version.Version] = version;
            }
        }

        return [.. allVersions.Values.OrderByDescending(version => version.Version)];
    }

    /// <inheritdoc/>
    public bool TryGetCachedVersions(string branch, TimeSpan maxAge, out List<int> versions)
    {
        versions = [];
        var normalizedBranch = NormalizeBranch(branch);
        string osName = LauncherUtilities.GetOS();
        string arch = LauncherUtilities.GetArch();

        var cached = _cache.TryGet(osName, arch);
        if (cached == null || !_cache.IsBranchFresh(cached, normalizedBranch, maxAge))
        {
            return false;
        }

        versions = GetMergedVersionList(cached, normalizedBranch);
        return versions.Count > 0;
    }

    /// <inheritdoc/>
    public bool TryGetCachedVersionEntries(string branch, TimeSpan maxAge, out List<CachedVersionEntry> versions)
    {
        versions = [];
        var normalizedBranch = NormalizeBranch(branch);
        string osName = LauncherUtilities.GetOS();
        string arch = LauncherUtilities.GetArch();

        var cached = _cache.TryGet(osName, arch);
        if (cached == null || !_cache.IsBranchFresh(cached, normalizedBranch, maxAge))
            return false;

        versions = GetMergedVersionEntries(cached, normalizedBranch);
        return versions.Count > 0;
    }

    /// <summary>
    /// Gets version list with source information (official vs mirror)
    /// </summary>
    /// <returns>A task that completes with the requested version list with sources</returns>
    public async Task<VersionListResponse> GetVersionListWithSourcesAsync(string branch, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);

        await GetVersionListAsync(normalizedBranch, ct);

        var snapshot = _cache.Current ?? _cache.Load();

        var response = new VersionListResponse
        {
            HasOfficialAccount = HasOfficialAccount,
            OfficialSourceAvailable = snapshot?.Data.Hytale?.Branches.ContainsKey(normalizedBranch) == true
                && snapshot.Data.Hytale.Branches[normalizedBranch].Count > 0,
            HasDownloadSources = HasDownloadSources(),
            EnabledMirrorCount = EnabledMirrorCount
        };

        if (snapshot == null)
        {
            return response;
        }

        var versionMap = new Dictionary<int, VersionInfo>();

        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                foreach (var v in mirrorVersions)
                {
                    versionMap[v.Version] = new VersionInfo
                    {
                        Version = v.Version,
                        VersionName = v.VersionName ?? versionMap.GetValueOrDefault(v.Version)?.VersionName,
                        Source = VersionSource.Mirror,
                        IsLatest = false
                    };
                }
            }
        }

        if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
        {
            foreach (var v in officialVersions)
            {
                versionMap[v.Version] = new VersionInfo
                {
                    Version = v.Version,
                    VersionName = v.VersionName ?? versionMap.GetValueOrDefault(v.Version)?.VersionName,
                    Source = VersionSource.Official,
                    IsLatest = false
                };
            }
        }

        var sortedVersions = versionMap.Values.OrderByDescending(v => v.Version).ToList();
        if (sortedVersions.Count > 0)
        {
            sortedVersions[0].IsLatest = true;
        }

        response.Versions = sortedVersions;
        return response;
    }

    /// <summary>
    /// Gets the source of versions for a branch
    /// </summary>
    /// <returns>The requested version source</returns>
    public VersionSource GetVersionSource(string branch)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();

        if (snapshot?.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var versions) == true && versions.Count > 0)
        {
            return VersionSource.Official;
        }

        return VersionSource.Mirror;
    }

    /// <summary>
    /// Gets the download URL for a specific version.
    /// Prefers official source if available
    /// </summary>
    /// <returns>The requested version download url, or null when unavailable</returns>
    public string? GetVersionDownloadUrl(string branch, int version)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();

        if (snapshot == null) return null;

        if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
        {
            var entry = officialVersions.FirstOrDefault(v => v.Version == version);
            if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
            {
                return entry.PwrUrl;
            }
        }

        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                var entry = mirrorVersions.FirstOrDefault(v => v.Version == version);
                if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
                {
                    return entry.PwrUrl;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the cached version entry for a specific version
    /// </summary>
    /// <returns>The cached version entry, or null when unavailable</returns>
    public CachedVersionEntry? GetVersionEntry(string branch, int version)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();

        if (snapshot == null) return null;

        if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
        {
            var entry = officialVersions.FirstOrDefault(v => v.Version == version);
            if (entry != null)
            {
                return entry;
            }
        }

        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                var entry = mirrorVersions.FirstOrDefault(v => v.Version == version);
                if (entry != null)
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<string> RefreshAndGetDownloadUrlAsync(string branch, int version, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);

        var url = GetVersionDownloadUrl(normalizedBranch, version);
        if (!string.IsNullOrEmpty(url))
        {
            Logger.Debug("Version", $"Using cached URL for {normalizedBranch} v{version}");
            return url;
        }

        Logger.Info("Version", $"No cached URL for {normalizedBranch} v{version}, refreshing cache...");
        await ForceRefreshCacheAsync(normalizedBranch, ct);

        url = GetVersionDownloadUrl(normalizedBranch, version);
        if (!string.IsNullOrEmpty(url))
        {
            Logger.Success("Version", $"Got URL for {normalizedBranch} v{version} after cache refresh");
            return url;
        }

        throw new Exception($"No download URL available for {normalizedBranch} v{version}. " +
            "The version may not exist or all sources are unavailable.");
    }

    /// <inheritdoc/>
    public async Task<CachedVersionEntry> RefreshAndGetVersionEntryAsync(string branch, int version, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);

        var entry = GetVersionEntry(normalizedBranch, version);
        if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
        {
            Logger.Debug("Version", $"Using cached entry for {normalizedBranch} v{version}");
            return entry;
        }

        Logger.Info("Version", $"No cached entry for {normalizedBranch} v{version}, refreshing cache...");
        await ForceRefreshCacheAsync(normalizedBranch, ct);

        entry = GetVersionEntry(normalizedBranch, version);
        if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
        {
            Logger.Success("Version", $"Got entry for {normalizedBranch} v{version} after cache refresh");
            return entry;
        }

        throw new Exception($"Version {normalizedBranch} v{version} not found in any source. " +
            "The version may not exist or all sources are unavailable.");
    }

    /// <inheritdoc/>
    public async Task ForceRefreshCacheAsync(string branch, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        string osName = LauncherUtilities.GetOS();
        string arch = LauncherUtilities.GetArch();

        _cache.Invalidate();

        await _versionFetchLock.WaitAsync(ct);
        try
        {
            await FetchVersionListCoreAsync(normalizedBranch, osName, arch, ct);
        }
        finally
        {
            _versionFetchLock.Release();
        }
    }

    /// <summary>
    /// Removes a specific version from the cache for a given mirror/source.
    /// Call this when a download fails with 404 to prevent showing unavailable versions
    /// </summary>
    /// <param name="branch">Branch name (e.g., "release", "pre-release")</param>
    /// <param name="version">Version number to invalidate</param>
    /// <param name="sourceId">Source ID (mirror ID or "official"). If null, removes from all sources</param>
    public void InvalidateVersionFromCache(string branch, int version, string? sourceId = null)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();
        if (snapshot == null) return;

        bool modified = false;

        if (sourceId == null || sourceId.Equals("official", StringComparison.OrdinalIgnoreCase))
        {
            if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
            {
                var removed = officialVersions.RemoveAll(v => v.Version == version);
                if (removed > 0)
                {
                    Logger.Info("Version", $"Invalidated v{version} from official cache for {normalizedBranch}");
                    modified = true;
                }
            }
        }

        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (sourceId != null && !mirror.MirrorId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                var removed = mirrorVersions.RemoveAll(v => v.Version == version);
                if (removed > 0)
                {
                    Logger.Info("Version", $"Invalidated v{version} from {mirror.MirrorId} cache for {normalizedBranch}");
                    modified = true;
                }
            }
        }

        var patchSnapshot = _cache.LoadPatches();
        if (patchSnapshot != null)
        {
            bool patchModified = false;

            if (sourceId == null || sourceId.Equals("official", StringComparison.OrdinalIgnoreCase))
            {
                if (patchSnapshot.Data.Hytale?.TryGetValue(normalizedBranch, out var officialPatches) == true)
                {
                    var removed = officialPatches.RemoveAll(p => p.To == version);
                    if (removed > 0) patchModified = true;
                }
            }

            foreach (var mirror in patchSnapshot.Data.Mirrors)
            {
                if (sourceId != null && !mirror.MirrorId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (mirror.Branches.TryGetValue(normalizedBranch, out var patches))
                {
                    var removed = patches.RemoveAll(p => p.To == version);
                    if (removed > 0) patchModified = true;
                }
            }

            if (patchModified)
            {
                _cache.SavePatches(patchSnapshot);
            }
        }

        if (modified)
        {
            _cache.Set(snapshot);
            _cache.Save(snapshot);
        }
    }

    /// <summary>
    /// Returns true if the specified branch uses diff-based patching (mirrors only).
    /// Full builds remain available for fresh installs when a branch also has diffs.
    /// </summary>
    /// <returns>true when the branch uses differential updates; otherwise false</returns>
    public bool IsDiffBasedBranch(string branch)
    {
        var normalizedBranch = NormalizeBranch(branch);
        return _selectedMirror?.IsDiffBasedBranch(normalizedBranch) ??
               GetMirrorSourcesSnapshot().FirstOrDefault()?.IsDiffBasedBranch(normalizedBranch) ?? false;
    }

    /// <summary>
    /// Gets download URL from mirror sources only.
    /// Used when official servers are down and we need explicit mirror fallback
    /// </summary>
    /// <returns>A task that completes with the requested mirror download url, or null when unavailable</returns>
    public async Task<string?> GetMirrorDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);

        var candidates = await GetMirrorCandidatesAsync(ct);
        foreach (var mirror in candidates)
        {
            try
            {
                var url = await mirror.GetDownloadUrlAsync(os, arch, normalizedBranch, version, ct);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (!ReferenceEquals(_selectedMirror, mirror))
                    {
                        _selectedMirror = mirror;
                        Logger.Info("Version", $"Switched active mirror to {mirror.SourceId} for {normalizedBranch} v{version}");
                    }

                    return url;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"Mirror {mirror.SourceId} failed for {normalizedBranch} v{version}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Gets diff patch URL from mirror sources for applying incremental updates
    /// </summary>
    /// <returns>A task that completes with the requested mirror diff url, or null when unavailable</returns>
    public async Task<string?> GetMirrorDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);

        var candidates = await GetMirrorCandidatesAsync(ct);
        foreach (var mirror in candidates)
        {
            try
            {
                var url = await mirror.GetDiffUrlAsync(os, arch, normalizedBranch, fromVersion, toVersion, ct);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (!ReferenceEquals(_selectedMirror, mirror))
                    {
                        _selectedMirror = mirror;
                        Logger.Info("Version", $"Switched active mirror to {mirror.SourceId} for {normalizedBranch} v{fromVersion}~{toVersion}");
                    }

                    return url;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"Mirror {mirror.SourceId} failed for {normalizedBranch} v{fromVersion}~{toVersion}: {ex.Message}");
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>?> GetMirrorRequestHeadersAsync(
        string url, CancellationToken ct = default)
    {
        var selected = _selectedMirror as JsonMirrorSource;
        if (selected?.OwnsDownloadUrl(url) == true)
            return await selected.GetResolvedRequestHeadersAsync(ct);

        foreach (var source in GetMirrorSourcesSnapshot().OfType<JsonMirrorSource>())
        {
            if (source.OwnsDownloadUrl(url))
                return await source.GetResolvedRequestHeadersAsync(ct);
        }

        return null;
    }

    private async Task<List<IVersionSource>> GetMirrorCandidatesAsync(CancellationToken ct)
    {
        var candidates = new List<IVersionSource>();

        var preferred = _selectedMirror ?? await SelectBestMirrorAsync(ct);
        if (preferred != null)
        {
            candidates.Add(preferred);
        }

        foreach (var mirror in GetMirrorSourcesSnapshot())
        {
            if (!candidates.Contains(mirror))
            {
                candidates.Add(mirror);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Get sequence of patches to apply for differential update
    /// </summary>
    /// <returns>The patch sequence</returns>
    public List<int> GetPatchSequence(int fromVersion, int toVersion)
    {
        var patches = new List<int>();
        for (int v = fromVersion + 1; v <= toVersion; v++)
        {
            patches.Add(v);
        }
        return patches;
    }

    /// <inheritdoc/>
    public async Task<List<CachedPatchStep>?> GetMirrorPatchPlanAsync(
        string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        if (fromVersion == toVersion)
            return [];

        var normalizedBranch = NormalizeBranch(branch);
        var os = LauncherUtilities.GetOS();
        var arch = LauncherUtilities.GetArch();
        await GetVersionListAsync(normalizedBranch, ct);

        var snapshot = _cache.LoadPatches();
        foreach (var mirror in await GetMirrorCandidatesAsync(ct))
        {
            var steps = snapshot?.Os.Equals(os, StringComparison.OrdinalIgnoreCase) == true &&
                        snapshot.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase)
                ? snapshot.Data.Mirrors.FirstOrDefault(entry => entry.MirrorId == mirror.SourceId)?
                    .Branches.GetValueOrDefault(normalizedBranch)
                : null;
            steps ??= await mirror.GetPatchChainAsync(os, arch, normalizedBranch, ct);

            var route = FindPatchRoute(steps, fromVersion, toVersion);
            if (route is null)
                continue;

            _selectedMirror = mirror;
            return route;
        }

        return null;
    }

    private static List<CachedPatchStep>? FindPatchRoute(
        IEnumerable<CachedPatchStep> steps, int fromVersion, int toVersion)
    {
        var outgoing = steps.Where(step => step.From < step.To && !string.IsNullOrWhiteSpace(step.PwrUrl))
            .GroupBy(step => step.From)
            .ToDictionary(group => group.Key, group => group.ToList());
        var previous = new Dictionary<int, CachedPatchStep>();
        var visited = new HashSet<int> { fromVersion };
        var pending = new Queue<int>();
        pending.Enqueue(fromVersion);

        while (pending.TryDequeue(out var from))
        {
            if (!outgoing.TryGetValue(from, out var nextSteps))
                continue;

            foreach (var step in nextSteps)
            {
                if (step.To > toVersion || !visited.Add(step.To))
                    continue;

                previous[step.To] = step;
                if (step.To == toVersion)
                {
                    var route = new List<CachedPatchStep>();
                    for (var current = toVersion; current != fromVersion; current = previous[current].From)
                        route.Add(previous[current]);
                    route.Reverse();
                    return route;
                }

                pending.Enqueue(step.To);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public bool IsOfficialServerDown(string branch)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();

        return snapshot?.Data.Hytale?.Branches.ContainsKey(normalizedBranch) != true
            || snapshot.Data.Hytale.Branches[normalizedBranch].Count == 0;
    }

    private static string NormalizeBranch(string branch)
    {
        return branch.ToLowerInvariant() switch
        {
            "release" => "release",
            "pre-release" => "pre-release",
            "prerelease" => "pre-release",
            "pre_release" => "pre-release",
            "beta" => "beta",
            "alpha" => "alpha",
            _ => "release"
        };
    }

    /// <summary>
    /// Tests the speed and availability of a mirror by ID
    /// </summary>
    /// <returns>A task that completes with the selected mirror speed test result</returns>
    public async Task<MirrorSpeedTestResult> TestMirrorSpeedAsync(string mirrorId, bool forceRefresh = false, CancellationToken ct = default)
    {
        var mirrors = GetMirrorSourcesSnapshot();
        var mirror = mirrors.FirstOrDefault(m => m.SourceId.Equals(mirrorId, StringComparison.OrdinalIgnoreCase));

        if (mirror == null)
        {
            Logger.Warning("Version", $"TestMirrorSpeedAsync: mirror '{mirrorId}' not found in {mirrors.Count} loaded sources");
            return new MirrorSpeedTestResult
            {
                MirrorId = mirrorId,
                MirrorName = mirrorId,
                PingMs = -1,
                SpeedMBps = 0,
                IsAvailable = false,
                TestedAt = DateTime.UtcNow
            };
        }

        Logger.Debug("Version", $"TestMirrorSpeedAsync: testing mirror '{mirrorId}' (forceRefresh={forceRefresh})");

        if (!forceRefresh)
        {
            var cached = mirror.GetCachedSpeedTest();
            if (cached != null)
            {
                Logger.Debug("Version", $"TestMirrorSpeedAsync: returning cached result for '{mirrorId}'");
                return cached;
            }
        }

        return await mirror.TestSpeedAsync(ct);
    }

    /// <summary>
    /// Tests the speed and availability of the official Hytale CDN
    /// </summary>
    /// <returns>A task that completes with the official source speed test result</returns>
    public async Task<MirrorSpeedTestResult> TestOfficialSpeedAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (_hytaleSource == null || !_hytaleSource.IsAvailable)
        {
            return new MirrorSpeedTestResult
            {
                MirrorId = "official",
                MirrorName = "Hytale",
                MirrorUrl = "https://cdn.hytale.com",
                PingMs = -1,
                SpeedMBps = 0,
                IsAvailable = false,
                TestedAt = DateTime.UtcNow
            };
        }

        if (!forceRefresh)
        {
            var cached = _hytaleSource.GetCachedSpeedTest();
            if (cached != null)
            {
                return cached;
            }
        }

        return await _hytaleSource.TestSpeedAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<MirrorSpeedTestResult> ProbeSourceAvailabilityAsync(
        string sourceId,
        CancellationToken ct = default)
    {
        IVersionSource? source = string.Equals(sourceId, "hytale", StringComparison.OrdinalIgnoreCase)
            ? _hytaleSource
            : GetMirrorSourcesSnapshot().FirstOrDefault(candidate =>
                string.Equals(candidate.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

        if (source is null || !source.IsAvailable)
        {
            return new MirrorSpeedTestResult
            {
                MirrorId = sourceId,
                MirrorName = sourceId,
                PingMs = -1,
                IsAvailable = false,
                TestedAt = DateTime.UtcNow
            };
        }

        var result = await source.ProbeAvailabilityAsync(ct);
        if (result.IsAvailable && source.Type == VersionSourceType.Mirror)
        {
            result.HasVersionsForCurrentPlatform = await CheckCurrentPlatformVersionsAsync(source, ct);
        }

        return result;
    }

    private static async Task<bool?> CheckCurrentPlatformVersionsAsync(
        IVersionSource source,
        CancellationToken ct)
    {
        var os = LauncherUtilities.GetOS();
        var arch = LauncherUtilities.GetArch();
        var checkFailed = false;

        foreach (var branch in new[] { "release", "pre-release" })
        {
            try
            {
                if ((await source.GetVersionsAsync(os, arch, branch, ct)).Count > 0)
                    return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                checkFailed = true;
                Logger.Debug(
                    "Version",
                    $"Platform version probe failed for '{source.SourceId}' ({os}/{arch}/{branch}): {ex.Message}");
            }
        }

        return checkFailed ? null : false;
    }

    /// <summary>
    /// Gets all available mirrors
    /// </summary>
    public List<(string Id, string Name)> GetAvailableMirrors()
    {
        return [.. GetMirrorSourcesSnapshot().Select(m => (m.SourceId, m.GetCachedSpeedTest()?.MirrorName ?? m.SourceId))];
    }

    /// <summary>
    /// Selects the best mirror based on speed tests.
    /// Only called when official source is not available
    /// </summary>
    /// <returns>A task that completes with the selected best mirror, or null when unavailable</returns>
    public async Task<IVersionSource?> SelectBestMirrorAsync(CancellationToken ct = default)
    {
        var mirrors = GetMirrorSourcesSnapshot();
        if (mirrors.Count == 0)
        {
            Logger.Warning("Version", "No mirrors available for selection");
            return null;
        }

        if (mirrors.Count == 1)
        {
            _selectedMirror = mirrors[0];
            Logger.Info("Version", $"Only one mirror available: {_selectedMirror.SourceId}");
            return _selectedMirror;
        }

        Logger.Info("Version", $"Testing {mirrors.Count} mirrors to select the best one...");

        var results = new List<(IVersionSource Source, MirrorSpeedTestResult Result)>();

        var tasks = mirrors.Select(async mirror =>
        {
            try
            {
                var result = await mirror.TestSpeedAsync(ct);
                return (Source: mirror, Result: result);
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"Mirror {mirror.SourceId} speed test failed: {ex.Message}");
                return (Source: mirror, Result: new MirrorSpeedTestResult
                {
                    MirrorId = mirror.SourceId,
                    MirrorName = mirror.SourceId,
                    IsAvailable = false,
                    PingMs = -1,
                    SpeedMBps = 0
                });
            }
        });

        var testResults = await Task.WhenAll(tasks);
        results.AddRange(testResults);

        var availableMirrors = results
            .Where(r => r.Result.IsAvailable && r.Result.SpeedMBps > 0)
            .OrderByDescending(r => r.Result.SpeedMBps)
            .ThenBy(r => r.Result.PingMs)
            .ToList();

        if (availableMirrors.Count == 0)
        {
            Logger.Warning("Version", "No mirrors passed speed test, using first available");
            _selectedMirror = mirrors[0];
            return _selectedMirror;
        }

        var (Source, Result) = availableMirrors[0];
        _selectedMirror = Source;
        Logger.Success("Version", $"Selected mirror: {Source.SourceId} ({Result.SpeedMBps:F2} MB/s, {Result.PingMs}ms ping)");

        return _selectedMirror;
    }

    /// <summary>
    /// Gets the currently selected mirror, or selects one if not yet selected
    /// </summary>
    /// <returns>A task that completes with the selected mirror, or null when unavailable</returns>
    public async Task<IVersionSource?> GetSelectedMirrorAsync(CancellationToken ct = default)
    {
        if (_selectedMirror != null)
            return _selectedMirror;

        return await SelectBestMirrorAsync(ct);
    }

    /// <inheritdoc/>
    public void ReloadMirrorSources()
    {
        Logger.Info("Version", "Reloading mirror sources from disk...");

        lock (_sourceSync)
        {
            foreach (var oldMirror in _mirrorSources)
                _sources.Remove(oldMirror);
            _mirrorSources.Clear();

            _selectedMirror = null;

            var freshMirrors = _mirrorCatalog?.CreateEnabledSources()
                               ?? MirrorCatalogLoader.LoadAll(_appDir, _httpClient);

            foreach (var mirror in freshMirrors.Where(m => m.Type == VersionSourceType.Mirror))
            {
                _mirrorSources.Add(mirror);
                _sources.Add(mirror);
            }

            _sources.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        Logger.Success("Version", $"Reloaded {EnabledMirrorCount} mirror sources");

        foreach (var source in GetMirrorSourcesSnapshot())
        {
            Logger.Debug("Version", $"Mirror {source.SourceId}: priority={source.Priority}");
        }

        ClearVersionCache();
    }

    /// <inheritdoc/>
    public bool HasDownloadSources()
    {
        return HasOfficialAccount || EnabledMirrorCount > 0;
    }

    /// <inheritdoc/>
    public int EnabledMirrorCount
    {
        get
        {
            lock (_sourceSync)
                return _mirrorSources.Count;
        }
    }

    private List<IVersionSource> GetSourcesSnapshot()
    {
        lock (_sourceSync)
            return [.. _sources];
    }

    private List<IVersionSource> GetMirrorSourcesSnapshot()
    {
        lock (_sourceSync)
            return [.. _mirrorSources];
    }

    private string[] GetMirrorSourceIdsSnapshot()
        => [.. GetMirrorSourcesSnapshot().Select(source => source.SourceId)];

    /// <inheritdoc/>
    public void ClearVersionCache()
    {
        try
        {
            _cache.Invalidate();

            var versionsPath = _cache.GetSnapshotPath();
            if (File.Exists(versionsPath))
            {
                File.Delete(versionsPath);
                Logger.Info("Version", "Deleted versions cache file");
            }

            var patchesPath = _cache.GetPatchSnapshotPath();
            if (File.Exists(patchesPath))
            {
                File.Delete(patchesPath);
                Logger.Info("Version", "Deleted patches cache file");
            }

            Logger.Success("Version", "Version cache cleared");
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to clear version cache: {ex.Message}");
        }
    }
}
