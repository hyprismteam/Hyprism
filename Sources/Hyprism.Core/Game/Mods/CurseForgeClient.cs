// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Game.Mods;

/// <summary>
/// Wraps low-level CurseForge API HTTP communication, including request construction,
/// file resolution, and download-URL derivation.
/// All high-level business logic (mapping, installation) remains in <see cref="ModManager"/>.
/// </summary>
internal sealed class CurseForgeClient
{
    #region Fields and Constructor

    private const string ApiBaseUrl = "https://api.curseforge.com";

    /// <summary>Hytale game identifier on CurseForge</summary>
    public const int HytaleGameId = 70216;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly Func<string> _getApiKey;

    /// <summary>
    /// Initialises the client.
    /// </summary>
    /// <param name="httpClient">Shared <see cref="HttpClient"/> instance.</param>
    /// <param name="getApiKey">Delegate that returns the current CurseForge API key.</param>
    public CurseForgeClient(HttpClient httpClient, Func<string> getApiKey)
    {
        _http = httpClient;
        _getApiKey = getApiKey;
    }

    #endregion

    #region Public Helpers

    /// <summary>
    /// Returns <c>true</c> when a CurseForge API key is configured; otherwise logs a warning and returns <c>false</c>.
    /// </summary>
    /// <returns>true when a CurseForge API key is configured; otherwise false</returns>
    public bool HasApiKey()
    {
        if (!string.IsNullOrEmpty(_getApiKey())) return true;
        Logger.Warning("CurseForgeClient", "CurseForge API key is not configured");
        return false;
    }

    /// <summary>
    /// Creates an <see cref="HttpRequestMessage"/> targeting the CurseForge v1 API
    /// with the required authentication and content-type headers.
    /// </summary>
    /// <returns>The created request</returns>
    public HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var request = new HttpRequestMessage(method, $"{ApiBaseUrl}{endpoint}");
        request.Headers.Add("x-api-key", _getApiKey());
        request.Headers.Add("Accept", "application/json");
        return request;
    }

    /// <summary>
    /// Resolves the best available <see cref="CurseForgeFile"/> for a given mod.
    /// When <paramref name="fileId"/> is specified the exact file is fetched first;
    /// if not found (deleted/expired) the method falls back to the latest uploaded file.
    /// </summary>
    /// <returns>A task that completes with the resolved file, or null when unavailable</returns>
    public async Task<CurseForgeFile?> ResolveFileAsync(
        string modId,
        string? fileId,
        string? preferredGameVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            var fileEndpoint = $"/v1/mods/{modId}/files/{fileId}";
            using var fileRequest = CreateRequest(HttpMethod.Get, fileEndpoint);
            using var fileResponse = await _http.SendAsync(fileRequest);

            if (fileResponse.IsSuccessStatusCode)
            {
                var fileJson = await fileResponse.Content.ReadAsStringAsync();
                var cfFileResp = JsonSerializer.Deserialize<CurseForgeFileResponse>(fileJson, JsonOptions);
                if (cfFileResp?.Data != null)
                    return cfFileResp.Data;
            }

            Logger.Warning("CurseForgeClient",
                $"Get file info returned {fileResponse.StatusCode} for mod {modId} file {fileId}, falling back to latest file");
        }

        var filesEndpoint = $"/v1/mods/{modId}/files?pageSize=50";
        using var filesRequest = CreateRequest(HttpMethod.Get, filesEndpoint);
        using var filesResponse = await _http.SendAsync(filesRequest);

        if (!filesResponse.IsSuccessStatusCode)
        {
            Logger.Warning("CurseForgeClient", $"Get latest mod file returned {filesResponse.StatusCode} for mod {modId}");
            return null;
        }

        var filesJson = await filesResponse.Content.ReadAsStringAsync();
        var filesResp = JsonSerializer.Deserialize<CurseForgeFilesResponse>(filesJson, JsonOptions);
        var files = filesResp?.Data ?? [];
        var latest = files.FirstOrDefault(file =>
            ModCompatibilityEvaluator.Evaluate(preferredGameVersion, file.GameVersions ?? []) ==
            ModCompatibilityStatus.Compatible) ?? files.FirstOrDefault();
        if (latest != null && !string.IsNullOrWhiteSpace(fileId))
        {
            Logger.Info("CurseForgeClient",
                $"Resolved mod {modId} to latest file {latest.Id} ('{latest.FileName}') instead of requested file {fileId}");
        }
        return latest;
    }

    /// <summary>
    /// Resolves the download URL for a specific mod file.
    /// <para>
    /// Priority: <paramref name="directUrl"/> (if present) → CurseForge download-url endpoint
    /// → deterministic edge.forgecdn.net CDN URL.
    /// </para>
    /// </summary>
    /// <returns>A task that completes with the resolved download url, or null when unavailable</returns>
    public async Task<string?> ResolveDownloadUrlAsync(string modId, string fileId, string? directUrl, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(directUrl))
            return directUrl;

        var endpoint = $"/v1/mods/{modId}/files/{fileId}/download-url";
        using var request = CreateRequest(HttpMethod.Get, endpoint);
        using var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var edgeFallbackOnError = BuildEdgeCdnFallbackUrl(fileId, fileName);
            if (!string.IsNullOrWhiteSpace(edgeFallbackOnError))
            {
                Logger.Info("CurseForgeClient", $"Falling back to deterministic CDN URL for mod {modId} file {fileId}");
                return edgeFallbackOnError;
            }
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var downloadUrlResp = JsonSerializer.Deserialize<CurseForgeDownloadUrlResponse>(json, JsonOptions);
        if (!string.IsNullOrWhiteSpace(downloadUrlResp?.Data))
            return downloadUrlResp.Data;

        var edgeFallback = BuildEdgeCdnFallbackUrl(fileId, fileName);
        if (!string.IsNullOrWhiteSpace(edgeFallback))
        {
            Logger.Info("CurseForgeClient", $"Download-url payload missing, using deterministic CDN URL for mod {modId} file {fileId}");
            return edgeFallback;
        }
        return null;
    }

    #endregion

    #region Static Helper

    /// <summary>
    /// Builds a deterministic edge.forgecdn.net CDN URL from the numeric file ID and file name.
    /// Returns <c>null</c> when the file ID is invalid or the file name is empty.
    /// </summary>
    /// <returns>The fallback CDN URL, or null when unavailable</returns>
    public static string? BuildEdgeCdnFallbackUrl(string fileId, string? fileName)
    {
        if (!int.TryParse(fileId, out var numericFileId) || numericFileId <= 0)
            return null;

        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var firstPart = numericFileId / 1000;
        var secondPart = numericFileId % 1000;
        var encodedFileName = Uri.EscapeDataString(fileName.Trim());
        return $"https://edge.forgecdn.net/files/{firstPart}/{secondPart}/{encodedFileName}";
    }

    #endregion
}
