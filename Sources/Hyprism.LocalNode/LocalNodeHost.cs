// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hyprism.Core;
using Hyprism.Core.Game.Authentication;
using Hyprism.Core.Game.Launch;
using LauncherLogger = Hyprism.Core.Infrastructure.Logger;

namespace Hyprism.LocalNode;

/// <summary>
/// Controls the dedicated Local Node process and exposes its control plane to Core
/// </summary>
public sealed class LocalNodeHost : ILocalNodeService, IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly LocalNodeOptions _options;
    private readonly string _controlSecret;
    private Process? _nodeProcess;
    private HttpClient? _controlClient;
    private X509Certificate2? _certificate;
    private X509Certificate2? _rootCertificate;
    private LocalNodeTrustStore? _trustStore;
    private bool _attachedToGame;
    private bool _disposed;

    /// <summary>
    /// Creates a Local Node below the Hyprism application data directory
    /// </summary>
    public LocalNodeHost(AppPathConfiguration appPath)
        : this(new LocalNodeOptions(
            Path.Combine(appPath.AppDir, "LocalNode"),
            LocalNodeEndpoint.Hostname,
            LocalNodeEndpoint.Port))
    {
    }

    /// <summary>
    /// Creates a Local Node controller with explicit endpoint options
    /// </summary>
    public LocalNodeHost(LocalNodeOptions options)
    {
        _options = options;
        _controlSecret = options.ControlSecret ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    /// <inheritdoc/>
    public string EndpointDomain => $"{_options.Hostname}:{_options.Port}";

    /// <inheritdoc/>
    public string Issuer => _options.Issuer;

    internal LocalNodeOptions Options => _options;

    /// <inheritdoc/>
    public async Task EnsureReadyAsync(
        string? gameDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_nodeProcess is { HasExited: false } && await IsHealthyAsync(cancellationToken))
                return;

            DisposeExitedProcess();
            PrepareControlPlane();

            if (await IsHealthyAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Another Local Node is already listening on {_options.Issuer}");
            }

            var assetsPath = ResolveAssetsPath(gameDirectory);
            _nodeProcess = StartNodeProcess(assetsPath);
            _attachedToGame = false;

            try
            {
                await WaitUntilReadyAsync(cancellationToken);
                LauncherLogger.Success("LocalNode", $"Dedicated process {_nodeProcess.Id} is ready");
            }
            catch
            {
                KillNodeProcess();
                throw;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OmniAuthSession> CreateSessionAsync(
        string playerUuid,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken: cancellationToken);
        using var response = await _controlClient!.PostAsJsonAsync("/v1/sessions", new
        {
            playerUuid,
            playerName
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<LocalSessionResponse>(cancellationToken);
        if (session is null
            || string.IsNullOrWhiteSpace(session.IdentityToken)
            || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            throw new InvalidOperationException("The Local Node returned an incomplete OmniAuth session");
        }

        return new OmniAuthSession(session.IdentityToken, session.SessionToken, session.ExpiresAt);
    }

    /// <inheritdoc/>
    public async Task AttachGameProcessAsync(
        int gameProcessId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nodeProcess is null || _nodeProcess.HasExited || _controlClient is null)
            throw new InvalidOperationException("The Local Node is not running");

        using var response = await _controlClient.PostAsJsonAsync("/v1/lifecycle/attach", new
        {
            gameProcessId
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"The Local Node rejected game process attachment ({(int)response.StatusCode}): {detail}");
        }

        _attachedToGame = true;
        LauncherLogger.Info("LocalNode", $"Lifetime transferred to game process {gameProcessId}");
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopNodeProcessAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void ApplyClientTrust(ProcessStartInfo startInfo)
    {
        if (_nodeProcess is null || _nodeProcess.HasExited || _trustStore is null)
            throw new InvalidOperationException("The Local Node must be started before applying client trust");
        _trustStore.Apply(startInfo);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        if (!_attachedToGame)
        {
            try
            {
                StopNodeProcessAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                KillNodeProcess();
            }
        }

        _disposed = true;
        DisposeResources();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (!_attachedToGame)
        {
            try
            {
                await StopNodeProcessAsync(CancellationToken.None);
            }
            catch
            {
                KillNodeProcess();
            }
        }

        _disposed = true;
        DisposeResources();
    }

    private void PrepareControlPlane()
    {
        _certificate ??= LocalNodeCertificateStore.LoadOrCreate(_options);
        _rootCertificate ??= LocalNodeCertificateStore.LoadRootCertificate(_options);
        _trustStore ??= LocalNodeTrustStore.Prepare(_options, _certificate, _rootCertificate);
        _controlClient ??= CreateControlClient(_certificate);
    }

    private Process StartNodeProcess(string? assetsPath)
    {
        var executableName = OperatingSystem.IsWindows()
            ? "Hyprism.LocalNode.exe"
            : "Hyprism.LocalNode";
        var executablePath = Path.Combine(AppContext.BaseDirectory, executableName);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!File.Exists(executablePath))
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Hyprism.LocalNode.dll");
            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException("The Local Node executable is missing", executablePath);
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(assemblyPath);
        }

        AddArgument(startInfo, "--data-directory", _options.DataDirectory);
        AddArgument(startInfo, "--hostname", _options.Hostname);
        AddArgument(startInfo, "--port", _options.Port.ToString());
        AddArgument(
            startInfo,
            "--certificate-directory",
            LocalNodeCertificateStore.GetCertificateDirectory(_options));
        AddArgument(
            startInfo,
            "--account-data-directory",
            _options.AccountDataDirectory ?? _options.DataDirectory);
        if (!string.IsNullOrWhiteSpace(_options.LogFilePath))
            AddArgument(startInfo, "--log-file", _options.LogFilePath);
        if (!string.IsNullOrWhiteSpace(_options.RequestJournalPath))
            AddArgument(startInfo, "--request-journal", _options.RequestJournalPath);
        AddArgument(startInfo, "--owner-pid", Environment.ProcessId.ToString());
        AddArgument(startInfo, "--control-secret", _controlSecret);
        if (!string.IsNullOrWhiteSpace(assetsPath))
            AddArgument(startInfo, "--assets-path", assetsPath);

        var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Could not start the Local Node process");
        }

        LauncherLogger.Info("LocalNode", $"Started dedicated process {process.Id}");
        return process;
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        while (startedAt.Elapsed < StartupTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_nodeProcess is null || _nodeProcess.HasExited)
            {
                var exitCode = _nodeProcess?.ExitCode;
                throw new InvalidOperationException(
                    $"The Local Node exited during startup with code {exitCode}. See '{ResolveLogFilePath()}'");
            }

            if (await IsHealthyAsync(cancellationToken))
                return;

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(
            $"The Local Node did not become ready within {StartupTimeout.TotalSeconds:0} seconds");
    }

    private string ResolveLogFilePath()
        => _options.LogFilePath ?? Path.Combine(_options.DataDirectory, "local-node.log");

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        if (_controlClient is null)
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
        try
        {
            using var response = await _controlClient.GetAsync("/health", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private async Task StopNodeProcessAsync(CancellationToken cancellationToken)
    {
        if (_nodeProcess is null || _nodeProcess.HasExited)
        {
            DisposeExitedProcess();
            return;
        }

        if (_controlClient is not null)
        {
            try
            {
                using var response = await _controlClient.PostAsync(
                    "/v1/lifecycle/stop",
                    content: null,
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await _nodeProcess.WaitForExitAsync(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                    DisposeExitedProcess();
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException
                                              or OperationCanceledException
                                              or TimeoutException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        KillNodeProcess();
    }

    private string? ResolveAssetsPath(string? gameDirectory)
    {
        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            var candidates = new[]
            {
                Path.Combine(gameDirectory, "Assets.zip"),
                Path.Combine(gameDirectory, "Client", "Assets.zip")
            };
            var selected = candidates.FirstOrDefault(File.Exists);
            if (selected is not null)
                return selected;
        }

        return !string.IsNullOrWhiteSpace(_options.AssetsPath) && File.Exists(_options.AssetsPath)
            ? Path.GetFullPath(_options.AssetsPath)
            : null;
    }

    private void KillNodeProcess()
    {
        if (_nodeProcess is not null)
        {
            try
            {
                if (!_nodeProcess.HasExited)
                    _nodeProcess.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
        DisposeExitedProcess(force: true);
    }

    private void DisposeExitedProcess(bool force = false)
    {
        if (_nodeProcess is null || (!force && !_nodeProcess.HasExited))
            return;
        _nodeProcess.Dispose();
        _nodeProcess = null;
        _attachedToGame = false;
    }

    private void DisposeResources()
    {
        _nodeProcess?.Dispose();
        _nodeProcess = null;
        _controlClient?.Dispose();
        _controlClient = null;
        _certificate?.Dispose();
        _certificate = null;
        _rootCertificate?.Dispose();
        _rootCertificate = null;
        _startGate.Dispose();
    }

    private HttpClient CreateControlClient(X509Certificate2 certificate)
    {
        var expectedCertificate = certificate.RawData;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented is not null && presented.RawData.AsSpan().SequenceEqual(expectedCertificate)
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{_options.Port}"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.Add("X-Hyprism-Control", _controlSecret);
        return client;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private sealed record LocalSessionResponse(
        string IdentityToken,
        string SessionToken,
        DateTimeOffset ExpiresAt);
}
