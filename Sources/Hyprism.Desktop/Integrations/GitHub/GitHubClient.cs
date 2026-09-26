// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Hyprism.Core.Infrastructure;
using Hyprism.Desktop.Platform;

namespace Hyprism.Desktop.Integrations.GitHub;

/// <summary>
/// Represents a public GitHub account returned by the contributors API
/// </summary>
public sealed class GitHubUser
{
    /// <summary>
    /// Gets the GitHub login
    /// </summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;

    /// <summary>
    /// Gets the avatar URL
    /// </summary>
    [JsonPropertyName("avatar_url")]
    public string AvatarUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the public profile URL
    /// </summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the GitHub account type
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of contributions counted by GitHub
    /// </summary>
    [JsonPropertyName("contributions")]
    public int Contributions { get; init; }
}
/// <summary>
/// Represents a commit displayed in the About page
/// </summary>
/// <param name="Sha">The full commit SHA</param>
/// <param name="Message">The first line of the commit message</param>
/// <param name="HtmlUrl">The public GitHub URL for the commit</param>
public sealed record GitHubCommit(string Sha, string Message, string HtmlUrl);

/// <summary>
/// Loads and caches public metadata for the Hyprism GitHub repository
/// </summary>
public sealed class GitHubClient : IGitHubClient
{
    private const string RepositoryApi = "https://api.github.com/repos/hyprismteam/Hyprism";

    private readonly HttpClient _httpClient;
    private readonly RemoteImageCache? _imageCache;
    private readonly object _cacheLock = new();
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _avatarCache = new();
    private Task<List<GitHubUser>>? _contributorsTask;
    private Task<GitHubCommit?>? _latestCommitTask;

    /// <summary>
    /// Initializes the GitHub integration
    /// </summary>
    /// <param name="httpClient">The shared client used for GitHub requests</param>
    /// <param name="imageCache">The persistent cache for contributor and team avatars</param>
    public GitHubClient(HttpClient httpClient, RemoteImageCache? imageCache = null)
    {
        _httpClient = httpClient;
        _imageCache = imageCache;
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.Add("User-Agent", DesktopApplicationInfo.UserAgent);
    }

    /// <inheritdoc />
    public async Task<List<GitHubUser>> GetContributorsAsync()
    {
        Task<List<GitHubUser>> task;
        lock (_cacheLock)
            task = _contributorsTask ??= LoadContributorsAsync();

        return [.. await task.ConfigureAwait(false)];
    }

    /// <inheritdoc />
    public async Task<GitHubCommit?> GetLatestMainCommitAsync()
    {
        Task<GitHubCommit?> task;
        lock (_cacheLock)
            task = _latestCommitTask ??= LoadLatestMainCommitAsync();

        return await task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GitHubUser?> GetUserAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        try
        {
            return await _httpClient
                .GetFromJsonAsync<GitHubUser>($"https://api.github.com/users/{Uri.EscapeDataString(username)}")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogFailure($"load GitHub user {username}", exception);
            return null;
        }
    }

    /// <inheritdoc />
    public Task<byte[]?> LoadAvatarAsync(string url, int decodeWidth = 96)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Task.FromResult<byte[]?>(null);

        var normalizedWidth = Math.Clamp(decodeWidth, 24, 512);
        var cacheKey = $"{uri.AbsoluteUri}|{normalizedWidth}";
        return _avatarCache.GetOrAdd(cacheKey, _ => LoadAvatarCoreAsync(uri, normalizedWidth));
    }

    private async Task<List<GitHubUser>> LoadContributorsAsync()
    {
        try
        {
            var contributors = await _httpClient
                                   .GetFromJsonAsync<List<GitHubUser>>(
                                       $"{RepositoryApi}/contributors?per_page=100")
                                   .ConfigureAwait(false)
                               ?? [];
            var recentLogins = await LoadRecentContributorLoginsAsync().ConfigureAwait(false);
            if (recentLogins.Count == 0)
                return contributors;

            var recentOrder = recentLogins
                .Select((login, index) => (login, index))
                .ToDictionary(item => item.login, item => item.index, StringComparer.OrdinalIgnoreCase);
            return contributors
                .Select((contributor, index) => new { contributor, index })
                .OrderBy(item => recentOrder.GetValueOrDefault(item.contributor.Login, int.MaxValue))
                .ThenBy(item => item.index)
                .Select(item => item.contributor)
                .ToList();
        }
        catch (Exception exception)
        {
            LogFailure("load GitHub contributors", exception);
            return [];
        }
    }

    private async Task<List<string>> LoadRecentContributorLoginsAsync()
    {
        try
        {
            var commits = await _httpClient
                              .GetFromJsonAsync<List<GitHubCommitAuthorResponse>>(
                                  $"{RepositoryApi}/commits?sha=main&per_page=100")
                              .ConfigureAwait(false)
                          ?? [];
            return commits
                .Select(commit => commit.Author?.Login)
                .Where(login => !string.IsNullOrWhiteSpace(login))
                .Select(login => login!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception)
        {
            LogFailure("load recent GitHub contributors", exception);
            return [];
        }
    }

    private async Task<GitHubCommit?> LoadLatestMainCommitAsync()
    {
        try
        {
            var response = await _httpClient
                .GetFromJsonAsync<GitHubCommitResponse>($"{RepositoryApi}/commits/main")
                .ConfigureAwait(false);
            if (response is null || string.IsNullOrWhiteSpace(response.Sha))
                return null;

            var message = response.Commit.Message
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?.Trim() ?? string.Empty;
            return new GitHubCommit(response.Sha, message, response.HtmlUrl);
        }
        catch (Exception exception)
        {
            LogFailure("load the latest main commit", exception);
            return null;
        }
    }

    private async Task<byte[]?> LoadAvatarCoreAsync(Uri uri, int decodeWidth)
    {
        try
        {
            var separator = string.IsNullOrEmpty(uri.Query) ? '?' : '&';
            var sizedUrl = $"{uri.AbsoluteUri}{separator}s={decodeWidth}";
            return _imageCache is null
                ? await _httpClient.GetByteArrayAsync(sizedUrl).ConfigureAwait(false)
                : await _imageCache.GetBytesAsync(sizedUrl, "github").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogFailure($"load GitHub avatar {uri}", exception);
            return null;
        }
    }

    private static void LogFailure(string operation, Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden })
        {
            Logger.Warning("GitHub", $"Failed to {operation}: rate limit exceeded");
            return;
        }

        Logger.Error("GitHub", $"Failed to {operation}: {exception}");
    }

    private sealed class GitHubCommitResponse
    {
        [JsonPropertyName("sha")]
        public string Sha { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("commit")]
        public GitHubCommitDetails Commit { get; init; } = new();
    }

    private sealed class GitHubCommitDetails
    {
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }

    private sealed class GitHubCommitAuthorResponse
    {
        [JsonPropertyName("author")]
        public GitHubCommitAuthor? Author { get; init; }
    }

    private sealed class GitHubCommitAuthor
    {
        [JsonPropertyName("login")]
        public string Login { get; init; } = string.Empty;
    }
}
