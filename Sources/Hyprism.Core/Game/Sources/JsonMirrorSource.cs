// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Game.Sources;

/// <summary>
/// Universal IVersionSource implementation driven by a MirrorMeta JSON descriptor.
/// Replaces all hardcoded mirror source classes (EstroGen, CobyLobby, ShipOfYarn, etc.).
/// Supports two source types:
/// <list type="bullet">
///   <item><b>pattern</b>: URL templates + version discovery (json-api / html-autoindex / static-list)</item>
///   <item><b>json-index</b>: Single API endpoint returning a full file index</item>
/// </list>
/// </summary>
public partial class JsonMirrorSource : IVersionSource
{
    private readonly MirrorMeta _meta;
    private readonly HttpClient _httpClient;

    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private readonly SemaphoreSlim _speedTestLock = new(1, 1);

    private readonly Dictionary<string, (DateTime CachedAt, List<DiscoveredVersion> Versions)> _versionCache = [];
    private readonly Dictionary<string, List<CachedPatchStep>> _manifestSteps = [];

    private sealed record DiscoveredVersion(int Build, string? Name);

    private JsonElement? _cachedJsonIndex;
    private DateTime _jsonIndexCachedAt = DateTime.MinValue;

    private MirrorSpeedTestResult? _speedTestResult;
    private MirrorSpeedTestResult? _availabilityResult;

    private TimeSpan CacheTtl => TimeSpan.FromMinutes(_meta.Cache.IndexTtlMinutes);
    private TimeSpan SpeedTestCacheTtl => TimeSpan.FromMinutes(_meta.Cache.SpeedTestTtlMinutes);
    private static readonly TimeSpan AvailabilityCacheTtl = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Creates a version source backed by a JSON mirror descriptor
    /// </summary>
    /// <param name="meta">The parsed mirror descriptor</param>
    /// <param name="httpClient">The HTTP client used for mirror requests</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="meta"/> or <paramref name="httpClient"/> is null</exception>
    public JsonMirrorSource(MirrorMeta meta, HttpClient httpClient)
    {
        _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Creates an HttpRequestMessage with custom headers from the mirror config.
    /// Expands the official launcher header placeholders.
    /// </summary>
    private async Task<HttpRequestMessage> CreateRequestWithHeadersAsync(
        HttpMethod method, string url, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(method, url);
        await ApplyCustomHeadersAsync(request, ct);
        return request;
    }

    /// <summary>
    /// Applies custom headers from the mirror config to an HttpRequestMessage.
    /// Expands the official launcher header placeholders.
    /// </summary>
    private async Task ApplyCustomHeadersAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        var headers = await GetResolvedRequestHeadersAsync(ct);
        if (headers is null) return;

        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    internal Task<Dictionary<string, string>?> GetResolvedRequestHeadersAsync(CancellationToken ct)
        => MirrorHeaderResolver.ResolveAsync(_meta.Headers, _httpClient, ct);

    internal bool OwnsDownloadUrl(string url)
    {
        var baseUrl = _meta.Pattern?.BaseUrl;
        return Uri.TryCreate(baseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var root) &&
               Uri.TryCreate(url, UriKind.Absolute, out var target) &&
               root.IsBaseOf(target);
    }

    /// <summary>
    /// Sends a GET request with custom headers applied
    /// </summary>
    private async Task<HttpResponseMessage> GetWithHeadersAsync(string url, CancellationToken ct = default)
    {
        using var request = await CreateRequestWithHeadersAsync(HttpMethod.Get, url, ct);
        return await _httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Sends a GET request with custom headers and ResponseHeadersRead option
    /// </summary>
    private async Task<HttpResponseMessage> GetWithHeadersStreamAsync(string url, CancellationToken ct = default)
    {
        using var request = await CreateRequestWithHeadersAsync(HttpMethod.Get, url, ct);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    #region IVersionSource Implementation

    /// <inheritdoc/>
    public string SourceId => _meta.Id;

    /// <inheritdoc/>
    public VersionSourceType Type => VersionSourceType.Mirror;

    /// <inheritdoc/>
    public bool IsAvailable => _meta.Enabled;

    /// <inheritdoc/>
    public int Priority => _meta.Priority;

    /// <inheritdoc/>
    public VersionSourceLayoutInfo LayoutInfo => BuildLayoutInfo();

    /// <inheritdoc/>
    public bool IsDiffBasedBranch(string branch)
    {
        var diffBranches = _meta.SourceType == "json-index"
            ? _meta.JsonIndex?.DiffBasedBranches
            : _meta.Pattern?.DiffBasedBranches;

        return diffBranches?.Any(b =>
            b.Equals(branch, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    /// <inheritdoc/>
    public async Task<List<CachedVersionEntry>> GetVersionsAsync(
        string os, string arch, string branch, CancellationToken ct = default)
    {
        return _meta.SourceType == "json-index"
            ? await GetVersionsFromJsonIndexAsync(os, arch, branch, ct)
            : await GetVersionsFromPatternAsync(os, arch, branch, ct);
    }

    /// <inheritdoc/>
    public async Task<string?> GetDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default)
    {
        if (_meta.SourceType == "json-index")
        {
            return await GetDownloadUrlFromJsonIndexAsync(os, arch, branch, version, ct);
        }

        if (_meta.Pattern!.VersionDiscovery.Method == "manifest")
        {
            await DiscoverVersionsAsync(os, arch, branch, ct);
            return _manifestSteps.GetValueOrDefault($"{os}:{arch}:{branch}")?
                .FirstOrDefault(step => step.From == 0 && step.To == version)?.PwrUrl;
        }

        if (_meta.Pattern.VersionDiscovery.Method == "head-probe" &&
            !(await DiscoverVersionsAsync(os, arch, branch, ct)).Any(entry => entry.Build == version))
            return null;

        return BuildPatternUrl(_meta.Pattern!.FullBuildUrl, os, arch, branch, version, 0, version);
    }

    /// <inheritdoc/>
    public async Task<string?> GetDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        if (_meta.SourceType == "json-index")
        {
            return await GetDiffUrlFromJsonIndexAsync(os, arch, branch, fromVersion, toVersion, ct);
        }

        if (_meta.Pattern!.VersionDiscovery.Method == "manifest")
        {
            await DiscoverVersionsAsync(os, arch, branch, ct);
            return _manifestSteps.GetValueOrDefault($"{os}:{arch}:{branch}")?
                .FirstOrDefault(step => step.From == fromVersion && step.To == toVersion)?.PwrUrl;
        }

        if (_meta.Pattern?.DiffPatchUrl == null) return null;
        return BuildPatternUrl(_meta.Pattern.DiffPatchUrl, os, arch, branch, 0, fromVersion, toVersion);
    }

    /// <inheritdoc/>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        if (_meta.SourceType == "json-index")
        {
            await FetchJsonIndexAsync(ct);
        }
        else
        {
            var os = LauncherUtilities.GetOS();
            var arch = LauncherUtilities.GetArch();
            await DiscoverVersionsAsync(os, arch, "release", ct);
        }
    }

    /// <inheritdoc/>
    public async Task<List<CachedPatchStep>> GetPatchChainAsync(
        string os, string arch, string branch, CancellationToken ct = default)
    {
        if (_meta.SourceType == "pattern" && _meta.Pattern?.VersionDiscovery.Method == "manifest")
        {
            await DiscoverVersionsAsync(os, arch, branch, ct);
            return _manifestSteps.GetValueOrDefault($"{os}:{arch}:{branch}") ?? [];
        }

        var steps = new List<CachedPatchStep>();

        try
        {
            var versions = await GetVersionsAsync(os, arch, branch, ct);
            if (versions.Count == 0) return steps;

            var sortedVersions = versions.Select(v => v.Version).OrderBy(v => v).ToList();

            bool hasDiffSupport = _meta.SourceType == "json-index"
                || _meta.Pattern?.DiffPatchUrl != null;

            if (hasDiffSupport)
            {
                int prev = 0;
                foreach (var ver in sortedVersions)
                {
                    var url = await GetDiffUrlAsync(os, arch, branch, prev, ver, ct);
                    if (!string.IsNullOrEmpty(url))
                    {
                        steps.Add(new CachedPatchStep
                        {
                            From = prev,
                            To = ver,
                            VersionName = versions.FirstOrDefault(version => version.Version == ver)?.VersionName,
                            PwrUrl = url
                        });
                    }
                    prev = ver;
                }
            }

            if (steps.Count == 0)
            {
                foreach (var ver in sortedVersions)
                {
                    var url = await GetDownloadUrlAsync(os, arch, branch, ver, ct);
                    if (!string.IsNullOrEmpty(url))
                    {
                        steps.Add(new CachedPatchStep
                        {
                            From = 0,
                            To = ver,
                            VersionName = versions.FirstOrDefault(version => version.Version == ver)?.VersionName,
                            PwrUrl = url
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JsonMirrorSource", $"GetPatchChainAsync failed for {branch}: {ex.Message}");
        }

        return steps;
    }

    /// <inheritdoc/>
    public MirrorSpeedTestResult? GetCachedSpeedTest()
    {
        if (_speedTestResult == null) return null;
        return DateTime.UtcNow - _speedTestResult.TestedAt <= SpeedTestCacheTtl
            ? _speedTestResult
            : null;
    }

    /// <inheritdoc/>
    public async Task<MirrorSpeedTestResult> TestSpeedAsync(CancellationToken ct = default)
    {
        var cached = GetCachedSpeedTest();
        if (cached != null) return cached;

        await _speedTestLock.WaitAsync(ct);
        try
        {
            cached = GetCachedSpeedTest();
            if (cached != null) return cached;

            var pingUrl = _meta.SpeedTest.PingUrl
                ?? (_meta.SourceType == "json-index" ? _meta.JsonIndex?.ApiUrl : _meta.Pattern?.BaseUrl)
                ?? "";

            var result = new MirrorSpeedTestResult
            {
                MirrorId = SourceId,
                MirrorUrl = pingUrl,
                MirrorName = _meta.Name,
                TestedAt = DateTime.UtcNow
            };

            try
            {
                var pingStart = DateTime.UtcNow;
                using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pingCts.CancelAfter(TimeSpan.FromSeconds(_meta.SpeedTest.PingTimeoutSeconds));

                using var pingReq = await CreateRequestWithHeadersAsync(HttpMethod.Head, pingUrl, pingCts.Token);
                using var pingResp = await _httpClient.SendAsync(pingReq, pingCts.Token);

                result.PingMs = (long)(DateTime.UtcNow - pingStart).TotalMilliseconds;

                var headSucceeded = pingResp.IsSuccessStatusCode ||
                    pingResp.StatusCode is HttpStatusCode.MethodNotAllowed
                        or HttpStatusCode.BadRequest
                        or HttpStatusCode.UnprocessableEntity;

                if (!headSucceeded)
                {
                    var getStart = DateTime.UtcNow;
                    using var getCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    getCts.CancelAfter(TimeSpan.FromSeconds(_meta.SpeedTest.PingTimeoutSeconds));

                    using var getReq = await CreateRequestWithHeadersAsync(HttpMethod.Get, pingUrl, getCts.Token);
                    using var getResp = await _httpClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, getCts.Token);

                    result.PingMs = (long)(DateTime.UtcNow - getStart).TotalMilliseconds;
                    result.IsAvailable = getResp.IsSuccessStatusCode;
                }
                else
                {
                    result.IsAvailable = true;
                }

                if (!result.IsAvailable)
                {
                    Logger.Warning($"Mirror:{SourceId}", $"Speed test ping failed, server not available");
                    _speedTestResult = result;
                    return result;
                }

                Logger.Debug($"Mirror:{SourceId}", $"Ping successful: {result.PingMs}ms, proceeding to speed test");

                var os = LauncherUtilities.GetOS();
                var arch = LauncherUtilities.GetArch();
                var testUrl = await GetSpeedTestUrlAsync(os, arch, ct);

                if (!string.IsNullOrEmpty(testUrl))
                {
                    int testSize = _meta.SpeedTest.SpeedTestSizeBytes;
                    var speedStart = DateTime.UtcNow;
                    using var speedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    speedCts.CancelAfter(TimeSpan.FromSeconds(30));

                    using var req = await CreateRequestWithHeadersAsync(HttpMethod.Get, testUrl, speedCts.Token);
                    req.Headers.Range = new RangeHeaderValue(0, testSize - 1);

                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, speedCts.Token);
                    if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.PartialContent)
                    {
                        await using var stream = await resp.Content.ReadAsStreamAsync(speedCts.Token);
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, speedCts.Token)) > 0)
                        {
                            totalRead += bytesRead;
                            if (totalRead >= testSize) break;
                        }

                        var elapsed = (DateTime.UtcNow - speedStart).TotalSeconds;
                        if (elapsed > 0 && totalRead > 0)
                        {
                            result.SpeedMBps = totalRead / 1_048_576.0 / elapsed;
                        }
                    }
                }

                Logger.Success($"Mirror:{SourceId}", $"Speed test: {result.PingMs}ms ping, {result.SpeedMBps:F2} MB/s");
            }
            catch (OperationCanceledException)
            {
                result.IsAvailable = false;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Mirror:{SourceId}", $"Speed test failed: {ex.Message}");
                result.IsAvailable = false;
            }

            _speedTestResult = result;
            return result;
        }
        finally
        {
            _speedTestLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<MirrorSpeedTestResult> ProbeAvailabilityAsync(CancellationToken ct = default)
    {
        if (_availabilityResult is not null &&
            DateTime.UtcNow - _availabilityResult.TestedAt <= AvailabilityCacheTtl)
        {
            return _availabilityResult;
        }

        var pingUrl = _meta.SpeedTest.PingUrl
            ?? (_meta.SourceType == "json-index" ? _meta.JsonIndex?.ApiUrl : _meta.Pattern?.BaseUrl)
            ?? string.Empty;
        var result = new MirrorSpeedTestResult
        {
            MirrorId = SourceId,
            MirrorUrl = pingUrl,
            MirrorName = _meta.Name,
            PingMs = -1,
            TestedAt = DateTime.UtcNow
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_meta.SpeedTest.PingTimeoutSeconds));
            var startedAt = DateTime.UtcNow;
            using var headRequest = await CreateRequestWithHeadersAsync(HttpMethod.Head, pingUrl, timeout.Token);
            using var headResponse = await _httpClient.SendAsync(
                headRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            result.PingMs = Math.Max(0, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);

            var statusCode = (int)headResponse.StatusCode;
            result.IsAvailable = headResponse.IsSuccessStatusCode || statusCode is 400 or 405 or 422;
            if (!result.IsAvailable)
            {
                startedAt = DateTime.UtcNow;
                using var getRequest = await CreateRequestWithHeadersAsync(HttpMethod.Get, pingUrl, timeout.Token);
                using var getResponse = await _httpClient.SendAsync(
                    getRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                result.PingMs = Math.Max(0, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);
                result.IsAvailable = getResponse.IsSuccessStatusCode;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result.IsAvailable = false;
        }
        catch (HttpRequestException ex)
        {
            Logger.Debug($"Mirror:{SourceId}", $"Availability probe failed: {ex.Message}");
            result.IsAvailable = false;
        }

        _availabilityResult = result;
        return result;
    }

    #endregion

    #region Pattern-based source methods

    private async Task<List<CachedVersionEntry>> GetVersionsFromPatternAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var versions = await DiscoverVersionsAsync(os, arch, branch, ct);
        var config = _meta.Pattern!;

        return [.. versions.Select(v => new CachedVersionEntry
        {
            Version = v.Build,
            VersionName = v.Name,
            FromVersion = 0,
            PwrUrl = config.VersionDiscovery.Method == "manifest"
                ? _manifestSteps.GetValueOrDefault($"{os}:{arch}:{branch}")?
                    .FirstOrDefault(step => step.From == 0 && step.To == v.Build)?.PwrUrl ?? ""
                : BuildPatternUrl(config.FullBuildUrl, os, arch, branch, v.Build, 0, v.Build),
            SigUrl = config.SignatureUrl != null
                ? BuildPatternUrl(config.SignatureUrl, os, arch, branch, v.Build, 0, v.Build)
                : null
        }).OrderByDescending(e => e.Version)];
    }

    /// <summary>
    /// Discovers available versions using the configured method
    /// </summary>
    private async Task<List<DiscoveredVersion>> DiscoverVersionsAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var config = _meta.Pattern!;
        var discovery = config.VersionDiscovery;

        string cacheKey = $"{os}:{arch}:{branch}";
        if (_versionCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
        {
            return cached.Versions;
        }

        await _fetchLock.WaitAsync(ct);
        try
        {
            if (_versionCache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            {
                return cached.Versions;
            }

            List<DiscoveredVersion> versions;

            switch (discovery.Method)
            {
                case "json-api":
                    versions = await DiscoverVersionsJsonApiAsync(os, arch, branch, ct);
                    break;
                case "html-autoindex":
                    versions = await DiscoverVersionsHtmlAsync(os, arch, branch, ct);
                    break;
                case "manifest":
                    versions = await DiscoverVersionsManifestAsync(os, arch, branch, ct);
                    break;
                case "head-probe":
                    versions = await DiscoverVersionsHeadProbeAsync(os, arch, branch, ct);
                    break;
                case "static-list":
                    versions = discovery.StaticVersions?
                        .Select(v => new DiscoveredVersion(v, null))
                        .OrderByDescending(v => v.Build)
                        .ToList() ?? [];
                    break;
                default:
                    Logger.Warning($"Mirror:{SourceId}", $"Unknown discovery method: {discovery.Method}");
                    versions = [];
                    break;
            }

            if (versions.Count > 0)
            {
                _versionCache[cacheKey] = (DateTime.UtcNow, versions);
                Logger.Success($"Mirror: {SourceId}", $"Discovered {versions.Count} versions for {branch}");
            }

            return versions;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning($"Mirror:{SourceId}", "Version discovery timed out");
            return _versionCache.TryGetValue(cacheKey, out var fb) ? fb.Versions : [];
        }
        catch (Exception ex)
        {
            Logger.Warning($"Mirror:{SourceId}", $"Version discovery failed: {ex.Message}");
            return _versionCache.TryGetValue(cacheKey, out var fb) ? fb.Versions : [];
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    private async Task<List<DiscoveredVersion>> DiscoverVersionsJsonApiAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var discovery = _meta.Pattern!.VersionDiscovery;
        if (string.IsNullOrEmpty(discovery.Url)) return [];

        var url = ApplyPlaceholders(discovery.Url, os, arch, branch, 0, 0, 0);
        Logger.Info($"Mirror:{SourceId}", $"Fetching versions from {url}...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await GetWithHeadersAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning($"Mirror:{SourceId}", $"API returned {response.StatusCode}");
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(cts.Token);

        var resolvedJsonPath = discovery.JsonPath != null
            ? ApplyPlaceholders(discovery.JsonPath, os, arch, branch, 0, 0, 0)
            : null;

        return ParseVersionsFromJson(json, resolvedJsonPath, discovery.BuildJsonPath);
    }

    private async Task<List<DiscoveredVersion>> DiscoverVersionsHtmlAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var discovery = _meta.Pattern!.VersionDiscovery;
        if (string.IsNullOrEmpty(discovery.Url)) return [];

        var url = ApplyPlaceholders(discovery.Url, os, arch, branch, 0, 0, 0);
        Logger.Info($"Mirror:{SourceId}", $"Fetching HTML index from {url}...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await GetWithHeadersAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning($"Mirror:{SourceId}", $"HTML index returned {response.StatusCode}");
            return [];
        }

        var html = await response.Content.ReadAsStringAsync(cts.Token);
        return ParseVersionsFromHtml(html, discovery.HtmlPattern, discovery.MinFileSizeBytes);
    }

    private async Task<List<DiscoveredVersion>> DiscoverVersionsHeadProbeAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var config = _meta.Pattern!;
        var maxVersion = Math.Clamp(config.VersionDiscovery.MaxProbeVersion, 1, 4096);
        if (!await HasFullBuildAsync(os, arch, branch, 1, ct))
            return [];

        var lastPresent = 1;
        var firstMissing = 2;
        while (firstMissing <= maxVersion &&
               await HasFullBuildAsync(os, arch, branch, firstMissing, ct))
        {
            lastPresent = firstMissing;
            firstMissing = Math.Min(firstMissing * 2, maxVersion + 1);
        }

        firstMissing = Math.Min(firstMissing, maxVersion + 1);
        while (lastPresent + 1 < firstMissing)
        {
            var middle = lastPresent + (firstMissing - lastPresent) / 2;
            if (await HasFullBuildAsync(os, arch, branch, middle, ct))
                lastPresent = middle;
            else
                firstMissing = middle;
        }

        var versions = new List<DiscoveredVersion>(lastPresent);
        for (var first = 1; first <= lastPresent; first += 8)
        {
            var candidates = Enumerable.Range(first, Math.Min(8, lastPresent - first + 1)).ToArray();
            var found = await Task.WhenAll(candidates.Select(version =>
                HasFullBuildAsync(os, arch, branch, version, ct)));
            for (var index = 0; index < candidates.Length; index++)
            {
                if (found[index])
                    versions.Add(new DiscoveredVersion(candidates[index], null));
            }
        }

        return [.. versions.OrderByDescending(version => version.Build)];
    }

    private async Task<bool> HasFullBuildAsync(
        string os, string arch, string branch, int version, CancellationToken ct)
    {
        var config = _meta.Pattern!;
        var url = BuildPatternUrl(config.FullBuildUrl, os, arch, branch, version, 0, version);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = await CreateRequestWithHeadersAsync(HttpMethod.Head, url, timeout.Token);
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();

        return response.Content.Headers.ContentLength is { } size &&
               size >= config.VersionDiscovery.MinFileSizeBytes &&
               response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) != true;
    }

    /// <summary>
    /// Discovers versions from a manifest.json file.
    /// Expects: { "files": { "{os}/{arch}/{branch}/{from}_to_{to}.pwr": { "size": N }, ... } }
    /// </summary>
    private async Task<List<DiscoveredVersion>> DiscoverVersionsManifestAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var discovery = _meta.Pattern!.VersionDiscovery;
        if (string.IsNullOrEmpty(discovery.Url)) return [];

        var url = ApplyPlaceholders(discovery.Url, os, arch, branch, 0, 0, 0);
        Logger.Info($"Mirror: {SourceId}", $"Fetching manifest from {url}...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await GetWithHeadersAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning($"Mirror: {SourceId}", $"Manifest returned {response.StatusCode}");
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(cts.Token);
        return ParseVersionsFromManifest(json, os, arch, branch);
    }

    /// <summary>
    /// Parses installable versions and actual patch edges from manifest.json.
    /// File paths: {os}/{arch}/{branch}/{from}_to_{to}.pwr
    /// Version metadata may also be published under versions[branch][build].version
    /// or files[...].gameVersion
    /// Returns only targets reachable from a full archive for the requested platform.
    /// </summary>
    private List<DiscoveredVersion> ParseVersionsFromManifest(string json, string os, string arch, string branch)
    {
        var cacheKey = $"{os}:{arch}:{branch}";
        _manifestSteps[cacheKey] = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var versions = new Dictionary<int, string?>();
            var steps = new List<CachedPatchStep>();
            var root = doc.RootElement;

            if (root.TryGetProperty("versions", out var versionsNode) &&
                versionsNode.ValueKind == JsonValueKind.Object)
            {
                var remoteBranch = ApplyMapping(_meta.Pattern?.BranchMapping, branch);
                if (TryGetPropertyIgnoreCase(versionsNode, remoteBranch, out var branchVersions) &&
                    branchVersions.ValueKind == JsonValueKind.Object)
                {
                    foreach (var version in branchVersions.EnumerateObject())
                    {
                        if (!int.TryParse(version.Name, out var build) || build <= 0)
                            continue;

                        versions[build] = TryGetManifestVersionName(version.Value);
                    }
                }
            }

            var hasFileIndex = root.TryGetProperty("files", out var filesNode) &&
                filesNode.ValueKind == JsonValueKind.Object;

            if (!hasFileIndex)
                return [];

            if (hasFileIndex)
            {
                var mappedOs = ApplyMapping(_meta.Pattern?.OsMapping, os);
                var mappedArch = ApplyMapping(_meta.Pattern?.ArchMapping, arch);
                var mappedBranch = ApplyMapping(_meta.Pattern?.BranchMapping, branch);
                var prefix = $"{mappedOs}/{mappedArch}/{mappedBranch}/";
                var patchPattern = PatchFileNameRegex();

                foreach (var file in filesNode.EnumerateObject())
                {
                    if (!file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var match = patchPattern.Match(file.Name);
                    if (!match.Success || match.Index != prefix.Length ||
                        !int.TryParse(match.Groups[1].Value, out var fromVersion) ||
                        !int.TryParse(match.Groups[2].Value, out var toVersion) ||
                        fromVersion < 0 || toVersion <= fromVersion)
                        continue;

                    var fileVersionName = TryGetManifestVersionName(file.Value);
                    if (!versions.TryGetValue(toVersion, out var existingName) ||
                        string.IsNullOrWhiteSpace(existingName))
                    {
                        versions[toVersion] = fileVersionName;
                    }

                    var template = fromVersion == 0
                        ? _meta.Pattern!.FullBuildUrl
                        : _meta.Pattern!.DiffPatchUrl;
                    if (string.IsNullOrWhiteSpace(template))
                        continue;

                    steps.Add(new CachedPatchStep
                    {
                        From = fromVersion,
                        To = toVersion,
                        PwrUrl = BuildPatternUrl(template, os, arch, branch, toVersion, fromVersion, toVersion)
                    });
                }

                var reachable = new HashSet<int> { 0 };
                var pending = new Queue<int>();
                pending.Enqueue(0);
                var outgoing = steps.GroupBy(step => step.From)
                    .ToDictionary(group => group.Key, group => group.ToList());
                while (pending.TryDequeue(out var from))
                {
                    if (!outgoing.TryGetValue(from, out var nextSteps))
                        continue;

                    foreach (var step in nextSteps)
                    {
                        if (reachable.Add(step.To))
                            pending.Enqueue(step.To);
                    }
                }

                steps = [.. steps.Where(step => reachable.Contains(step.From) && reachable.Contains(step.To))];
                foreach (var step in steps)
                    step.VersionName = versions.GetValueOrDefault(step.To);

                versions = versions.Where(pair => reachable.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }

            _manifestSteps[cacheKey] = steps;

            if (versions.Count == 0)
            {
                Logger.Warning($"Mirror: {SourceId}", "Manifest contains no usable version entries");
                return [];
            }

            Logger.Debug($"Mirror: {SourceId}", $"Manifest: found {versions.Count} versions for {branch}");
            return [.. versions
                .OrderByDescending(pair => pair.Key)
                .Select(pair => new DiscoveredVersion(pair.Key, pair.Value))];
        }
        catch (Exception ex)
        {
            Logger.Warning($"Mirror: {SourceId}", $"Failed to parse manifest: {ex.Message}");
            return [];
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetManifestVersionName(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) || int.TryParse(text, out _)
                ? null
                : text;
        }

        if (value.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var fieldName in new[]
        {
            "version", "gameVersion", "game_version", "versionName", "version_name",
            "displayVersion", "display_version", "name"
        })
        {
            if (TryGetPropertyIgnoreCase(value, fieldName, out var field) &&
                field.ValueKind == JsonValueKind.String)
            {
                var text = field.GetString();
                if (!string.IsNullOrWhiteSpace(text) && !int.TryParse(text, out _))
                    return text;
            }
        }

        foreach (var fieldName in new[] { "meta", "metadata" })
        {
            if (TryGetPropertyIgnoreCase(value, fieldName, out var metadata))
            {
                var nestedName = TryGetManifestVersionName(metadata);
                if (!string.IsNullOrWhiteSpace(nestedName))
                    return nestedName;
            }
        }

        return null;
    }

    private static string ApplyMapping(Dictionary<string, string>? mapping, string value)
    {
        if (mapping != null && mapping.TryGetValue(value, out var mapped))
            return mapped;
        return value;
    }

    /// <summary>
    /// Parses version names and numeric builds from a JSON response using the configured jsonPath.
    /// Supports:
    /// - "items[].version" - array of objects with a version field
    /// - "versions" - simple property name pointing to an array
    /// - "platform.branch.newest" - dot-notation nested path to a single value
    /// - "$root" or null - root is an array
    /// </summary>
    private List<DiscoveredVersion> ParseVersionsFromJson(
        string json,
        string? jsonPath,
        string? buildJsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var versions = new List<DiscoveredVersion>();

            if (jsonPath != null && jsonPath.Contains("[]."))
            {
                var parts = jsonPath.Split("[].");
                var arrayName = parts[0];
                var fieldName = parts[1];
                var buildFieldName = GetPathLeaf(buildJsonPath);

                JsonElement array;
                if (string.IsNullOrEmpty(arrayName) || arrayName == "$root")
                    array = root;
                else if (!root.TryGetProperty(arrayName, out array) || array.ValueKind != JsonValueKind.Array)
                    return versions;

                foreach (var item in array.EnumerateArray())
                {
                    if (TryCreateDiscoveredVersion(item, fieldName, buildFieldName, out var version))
                        versions.Add(version);
                }

                return SortDiscoveredVersions(versions);
            }

            if (jsonPath == null || jsonPath == "$root")
            {
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in root.EnumerateArray())
                    {
                        if (TryCreateDiscoveredVersion(el, null, null, out var version))
                            versions.Add(version);
                    }
                }
                return SortDiscoveredVersions(versions);
            }

            if (jsonPath.Contains('.'))
            {
                var pathParts = jsonPath.Split('.');
                JsonElement current = root;

                foreach (var part in pathParts)
                {
                    if (current.ValueKind != JsonValueKind.Object)
                    {
                        Logger.Debug($"Mirror: {SourceId}", $"JsonPath '{jsonPath}': expected object at '{part}', got {current.ValueKind}");
                        return versions;
                    }

                    if (!current.TryGetProperty(part, out current))
                    {
                        Logger.Debug($"Mirror: {SourceId}", $"JsonPath '{jsonPath}': property '{part}' not found");
                        return versions;
                    }
                }

                if (current.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in current.EnumerateArray())
                    {
                        if (TryCreateDiscoveredVersion(el, null, GetPathLeaf(buildJsonPath), out var version))
                            versions.Add(version);
                    }
                }
                else if (TryCreateDiscoveredVersion(current, null, GetPathLeaf(buildJsonPath), out var singleVersion))
                {
                    versions.Add(singleVersion);
                }

                return SortDiscoveredVersions(versions);
            }

            if (!root.TryGetProperty(jsonPath, out var versionsArray) || versionsArray.ValueKind != JsonValueKind.Array)
                return versions;

            foreach (var el in versionsArray.EnumerateArray())
            {
                if (TryCreateDiscoveredVersion(el, null, GetPathLeaf(buildJsonPath), out var version))
                    versions.Add(version);
            }

            return SortDiscoveredVersions(versions);
        }
        catch (JsonException ex)
        {
            Logger.Warning($"Mirror: {SourceId}", $"Failed to parse JSON: {ex.Message}");
            return [];
        }
    }

    private static string? GetPathLeaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var leaf = path.Split("[].", StringSplitOptions.RemoveEmptyEntries).Last();
        return leaf.Split('.').Last();
    }

    private static bool TryCreateDiscoveredVersion(
        JsonElement value,
        string? versionFieldName,
        string? buildFieldName,
        out DiscoveredVersion version)
    {
        version = new DiscoveredVersion(0, null);

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericValue))
        {
            version = new DiscoveredVersion(numericValue, null);
            return true;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), out numericValue))
        {
            version = new DiscoveredVersion(numericValue, null);
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object)
            return false;

        JsonElement versionValue = value;
        var resolvedVersionFieldName = versionFieldName;
        if (string.IsNullOrWhiteSpace(resolvedVersionFieldName))
        {
            resolvedVersionFieldName = new[] { "version", "versionName", "name" }
                .FirstOrDefault(name => value.TryGetProperty(name, out _));
        }

        if (!string.IsNullOrWhiteSpace(resolvedVersionFieldName) &&
            !value.TryGetProperty(resolvedVersionFieldName, out versionValue))
        {
            return false;
        }

        var versionName = versionValue.ValueKind == JsonValueKind.String
            ? versionValue.GetString()
            : null;

        if (versionValue.ValueKind == JsonValueKind.Number && versionValue.TryGetInt32(out numericValue))
            version = new DiscoveredVersion(numericValue, TryGetVersionName(value, resolvedVersionFieldName));
        else if (!string.IsNullOrWhiteSpace(versionName) && int.TryParse(versionName, out numericValue))
            version = new DiscoveredVersion(numericValue, null);
        else if (TryGetBuild(value, buildFieldName, out numericValue))
            version = new DiscoveredVersion(numericValue, versionName);
        else
            return false;

        return version.Build > 0;
    }

    private static string? TryGetVersionName(JsonElement value, string? versionFieldName)
    {
        foreach (var fieldName in new[] { "versionName", "version_name", "version", "displayVersion", "display_version", "name" })
        {
            if (string.Equals(fieldName, versionFieldName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (value.TryGetProperty(fieldName, out var field) && field.ValueKind == JsonValueKind.String)
            {
                var name = field.GetString();
                if (!string.IsNullOrWhiteSpace(name) && !int.TryParse(name, out _))
                    return name;
            }
        }

        foreach (var fieldName in new[] { "meta", "metadata" })
        {
            if (value.TryGetProperty(fieldName, out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            {
                var nestedName = TryGetVersionName(metadata, versionFieldName: null);
                if (!string.IsNullOrWhiteSpace(nestedName))
                    return nestedName;
            }
        }

        return null;
    }

    private static bool TryGetBuild(JsonElement value, string? explicitFieldName, out int build)
    {
        build = 0;
        var fieldNames = new[]
        {
            explicitFieldName,
            "build",
            "buildNumber",
            "build_number",
            "buildVersion",
            "build_version",
            "buildId",
            "build_id",
            "versionNumber",
            "version_number",
            "id",
            "number"
        };

        foreach (var fieldName in fieldNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (!value.TryGetProperty(fieldName!, out var field))
                continue;

            if (field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out build))
                return build > 0;

            if (field.ValueKind == JsonValueKind.String && int.TryParse(field.GetString(), out build))
                return build > 0;
        }

        return false;
    }

    private static List<DiscoveredVersion> SortDiscoveredVersions(IEnumerable<DiscoveredVersion> versions)
        => [.. versions
            .Where(version => version.Build > 0)
            .GroupBy(version => version.Build)
            .Select(group => group.FirstOrDefault(version => !string.IsNullOrWhiteSpace(version.Name)) ?? group.First())
            .OrderByDescending(version => version.Build)];

    /// <summary>
    /// Parses version numbers from HTML using the configured regex pattern
    /// </summary>
    private static List<DiscoveredVersion> ParseVersionsFromHtml(string html, string? pattern, long minFileSize)
    {
        if (string.IsNullOrEmpty(pattern)) return [];

        try
        {
            var versions = new List<DiscoveredVersion>();
            var regex = new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

            foreach (Match match in regex.Matches(html))
            {
                if (match.Groups.Count < 2) continue;
                if (!int.TryParse(match.Groups[1].Value, out int version)) continue;

                if (minFileSize > 0 && match.Groups.Count > 2
                    && long.TryParse(match.Groups[2].Value, out long fileSize)
                    && fileSize < minFileSize)
                {
                    continue;
                }

                versions.Add(new DiscoveredVersion(version, null));
            }

            return [.. versions
                .DistinctBy(version => version.Build)
                .OrderByDescending(version => version.Build)];
        }
        catch (ArgumentException ex)
        {
            Logger.Warning("Mirror", $"Invalid HTML version pattern: {ex.Message}");
            return [];
        }
        catch (RegexMatchTimeoutException)
        {
            Logger.Warning("Mirror", "HTML version pattern exceeded the one-second match limit");
            return [];
        }
    }

    /// <summary>
    /// Applies placeholder substitution to a URL template
    /// </summary>
    private string ApplyPlaceholders(string template, string os, string arch, string branch,
        int version, int from, int to)
    {
        var config = _meta.Pattern;
        var mappedOs = config?.OsMapping != null && config.OsMapping.TryGetValue(os, out var mo) ? mo : os;
        var mappedArch = config?.ArchMapping != null && config.ArchMapping.TryGetValue(arch, out var ma) ? ma : arch;
        var mappedBranch = config?.BranchMapping != null && config.BranchMapping.TryGetValue(branch, out var mb) ? mb : branch;

        return template
            .Replace("{base}", config?.BaseUrl ?? "")
            .Replace("{os}", mappedOs)
            .Replace("{arch}", mappedArch)
            .Replace("{branch}", mappedBranch)
            .Replace("{version}", version.ToString())
            .Replace("{from}", from.ToString())
            .Replace("{to}", to.ToString());
    }

    private string BuildPatternUrl(string template, string os, string arch, string branch,
        int version, int from, int to)
    {
        return ApplyPlaceholders(template, os, arch, branch, version, from, to);
    }

    #endregion

    #region JSON-index-based source methods

    private async Task<List<CachedVersionEntry>> GetVersionsFromJsonIndexAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var config = _meta.JsonIndex!;
        var entries = new List<CachedVersionEntry>();

        if (IsDiffBasedBranch(branch))
        {
            var patchFiles = await GetIndexFilesAsync(os, branch, "patch", ct);
            foreach (var (fileName, url) in patchFiles)
            {
                if (TryParseDiffFileName(fileName, os, arch, out int fromVer, out int toVer))
                {
                    entries.Add(new CachedVersionEntry
                    {
                        Version = toVer,
                        FromVersion = fromVer,
                        PwrUrl = url
                    });
                }
            }

            return [.. entries.OrderByDescending(e => e.Version).ThenBy(e => e.FromVersion)];
        }

        var baseFiles = config.Structure == "grouped"
            ? await GetIndexFilesAsync(os, branch, "base", ct)
            : await GetIndexFilesAsync(os, branch, null, ct);

        foreach (var (fileName, url) in baseFiles)
        {
            if (TryParseBaseFileName(fileName, os, arch, out int version))
            {
                entries.Add(new CachedVersionEntry
                {
                    Version = version,
                    FromVersion = 0,
                    PwrUrl = url
                });
            }
        }

        return [.. entries
            .GroupBy(e => e.Version)
            .Select(g => g.OrderBy(e => e.FromVersion).First())
            .OrderByDescending(e => e.Version)];
    }

    private async Task<string?> GetDownloadUrlFromJsonIndexAsync(
        string os, string arch, string branch, int version, CancellationToken ct)
    {
        var config = _meta.JsonIndex!;

        var baseFiles = config.Structure == "grouped"
            ? await GetIndexFilesAsync(os, branch, "base", ct)
            : await GetIndexFilesAsync(os, branch, null, ct);

        var baseKey = BuildBaseFileNameFromPattern(version, os, arch);
        return baseFiles.TryGetValue(baseKey, out var url) ? url : null;
    }

    private async Task<string?> GetDiffUrlFromJsonIndexAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct)
    {
        var files = await GetIndexFilesAsync(os, branch, "patch", ct);
        var key = BuildDiffFileNameFromPattern(fromVersion, toVersion, os, arch);
        return files.TryGetValue(key, out var url) ? url : null;
    }

    /// <summary>
    /// Extracts file entries from the cached JSON index
    /// </summary>
    private async Task<Dictionary<string, string>> GetIndexFilesAsync(
        string os, string branch, string? group, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = await GetOrFetchJsonIndexAsync(ct);
        if (root == null) return result;

        var config = _meta.JsonIndex!;
        var rootEl = root.Value;

        if (!rootEl.TryGetProperty(config.RootPath, out var gameNode) || gameNode.ValueKind != JsonValueKind.Object)
            return result;
        if (!gameNode.TryGetProperty(branch, out var branchNode) || branchNode.ValueKind != JsonValueKind.Object)
            return result;

        var platformKey = config.PlatformMapping != null && config.PlatformMapping.TryGetValue(os, out var pk) ? pk : os;
        if (!branchNode.TryGetProperty(platformKey, out var platformNode) || platformNode.ValueKind != JsonValueKind.Object)
            return result;

        JsonElement fileMap;
        if (group != null)
        {
            if (!platformNode.TryGetProperty(group, out fileMap) || fileMap.ValueKind != JsonValueKind.Object)
                return result;
        }
        else
        {
            fileMap = platformNode;
        }

        foreach (var prop in fileMap.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var url = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    result[prop.Name] = url;
            }
        }

        return result;
    }

    private async Task<JsonElement?> GetOrFetchJsonIndexAsync(CancellationToken ct)
    {
        if (_cachedJsonIndex.HasValue && DateTime.UtcNow - _jsonIndexCachedAt < CacheTtl)
            return _cachedJsonIndex;
        return await FetchJsonIndexAsync(ct);
    }

    private async Task<JsonElement?> FetchJsonIndexAsync(CancellationToken ct)
    {
        await _fetchLock.WaitAsync(ct);
        try
        {
            if (_cachedJsonIndex.HasValue && DateTime.UtcNow - _jsonIndexCachedAt < CacheTtl)
                return _cachedJsonIndex;

            var apiUrl = _meta.JsonIndex!.ApiUrl;
            Logger.Info($"Mirror:{SourceId}", $"Fetching JSON index from {apiUrl}...");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await GetWithHeadersAsync(apiUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"Mirror:{SourceId}", $"API returned {response.StatusCode}");
                return _cachedJsonIndex;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            _cachedJsonIndex = doc.RootElement.Clone();
            _jsonIndexCachedAt = DateTime.UtcNow;

            Logger.Success($"Mirror:{SourceId}", "JSON index loaded");
            return _cachedJsonIndex;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning($"Mirror:{SourceId}", "JSON index request timed out");
            return _cachedJsonIndex;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Mirror:{SourceId}", $"Failed to fetch JSON index: {ex.Message}");
            return _cachedJsonIndex;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    private static bool TryParseBaseFileName(string fileName, string os, string arch, out int version)
    {
        version = 0;
        var fileOs = NormalizeFileOs(os);
        var suffix = $"-{fileOs}-{arch}.pwr";
        if (!fileName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var numPart = fileName[1..^suffix.Length];
        return int.TryParse(numPart, out version);
    }

    private static bool TryParseDiffFileName(string fileName, string os, string arch, out int fromVer, out int toVer)
    {
        fromVer = 0;
        toVer = 0;
        var fileOs = NormalizeFileOs(os);
        var suffix = $"-{fileOs}-{arch}.pwr";
        if (!fileName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var core = fileName[1..^suffix.Length];
        var tilde = core.IndexOf('~');
        if (tilde <= 0) return false;

        return int.TryParse(core[..tilde], out fromVer) && int.TryParse(core[(tilde + 1)..], out toVer);
    }

    private static string BuildBaseFileNameFromPattern(int version, string os, string arch)
        => $"v{version}-{NormalizeFileOs(os)}-{arch}.pwr";

    private static string BuildDiffFileNameFromPattern(int from, int to, string os, string arch)
        => $"v{from}~{to}-{NormalizeFileOs(os)}-{arch}.pwr";

    private static string NormalizeFileOs(string os)
        => os.Equals("darwin", StringComparison.OrdinalIgnoreCase) ? "darwin" : os.ToLowerInvariant();

    #endregion

    #region Helpers

    private async Task<string?> GetSpeedTestUrlAsync(string os, string arch, CancellationToken ct)
    {
        try
        {
            var entries = await GetVersionsAsync(os, arch, "pre-release", ct);
            if (entries.Count > 0) return entries[0].PwrUrl;

            entries = await GetVersionsAsync(os, arch, "release", ct);
            if (entries.Count > 0) return entries[0].PwrUrl;
        }
        catch (Exception ex)
        {
            Logger.Debug($"Mirror:{SourceId}", $"Failed to get speed test URL: {ex.Message}");
        }
        return null;
    }

    private VersionSourceLayoutInfo BuildLayoutInfo()
    {
        if (_meta.SourceType == "json-index")
        {
            return new VersionSourceLayoutInfo
            {
                FullBuildLocation = $"JSON Index API: {_meta.JsonIndex?.ApiUrl}",
                PatchLocation = $"JSON Index API groups: base/patch",
                CachePolicy = $"In-memory JSON index cache TTL {_meta.Cache.IndexTtlMinutes}m; speed test cache TTL {_meta.Cache.SpeedTestTtlMinutes}m"
            };
        }

        return new VersionSourceLayoutInfo
        {
            FullBuildLocation = $"Pattern: {_meta.Pattern?.FullBuildUrl}",
            PatchLocation = $"Pattern: {_meta.Pattern?.DiffPatchUrl ?? "N/A"}",
            CachePolicy = $"In-memory version cache TTL {_meta.Cache.IndexTtlMinutes}m; speed test cache TTL {_meta.Cache.SpeedTestTtlMinutes}m"
        };
    }

    [GeneratedRegex(@"(\d+)_to_(\d+)\.pwr$")]
    private static partial Regex PatchFileNameRegex();

    #endregion
}
