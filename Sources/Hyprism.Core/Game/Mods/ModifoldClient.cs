// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Mods;

/// <summary>Reads public Hytale mod projects and versions from Modifold.</summary>
internal sealed class ModifoldClient(HttpClient http)
{
    private const string ApiBase = "https://api.modifold.com";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Searches public Modifold mod projects.</summary>
    /// <param name="query">Search text.</param>
    /// <param name="page">Zero-based page number.</param>
    /// <param name="pageSize">Maximum number of projects.</param>
    /// <param name="sortField">Launcher sort field.</param>
    /// <param name="tag">Optional Modifold category tag.</param>
    /// <returns>Matching projects and pagination information.</returns>
    public async Task<ModSearchResult> SearchAsync(
        string query, int page, int pageSize, int sortField, string? tag = null)
    {
        var sort = sortField switch
        {
            3 => "updated",
            11 => "newest",
            1 when !string.IsNullOrWhiteSpace(query) => "relevance",
            _ => "downloads"
        };
        var path = $"/projects?limit={pageSize}&page={page + 1}&project_type=mod&sort={sort}";
        if (!string.IsNullOrWhiteSpace(query))
            path += $"&search={Uri.EscapeDataString(query.Trim())}";
        if (!string.IsNullOrWhiteSpace(tag))
            path += $"&tags={Uri.EscapeDataString(tag)}";

        var result = await GetAsync<ProjectPage>(path);
        var projects = result.Projects ?? [];
        var mods = projects
            .Where(project => string.Equals(project.ProjectType, "mod", StringComparison.OrdinalIgnoreCase))
            .Select(project => MapProject(project))
            .ToList();
        var hasMore = result.Pagination?.HasMore ?? result.CurrentPage < result.TotalPages;
        return new ModSearchResult
        {
            Mods = mods,
            TotalCount = result.TotalPages * pageSize,
            HasMore = hasMore
        };
    }

    /// <summary>Reads one Modifold project.</summary>
    /// <param name="projectId">Project identifier.</param>
    /// <returns>Project metadata and versions.</returns>
    public async Task<ModInfo> GetModAsync(string projectId)
        => MapProject(await GetProjectAsync(projectId), includeVersions: true);

    /// <summary>Reads files for one Modifold project.</summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="page">Zero-based page number.</param>
    /// <param name="pageSize">Maximum number of files.</param>
    /// <returns>Available files and pagination information.</returns>
    public async Task<ModFilesResult> GetFilesAsync(string projectId, int page, int pageSize)
    {
        var project = await GetProjectAsync(projectId);
        var files = MapFiles(project).ToList();
        return new ModFilesResult
        {
            Files = files.Skip(page * pageSize).Take(pageSize).ToList(),
            TotalCount = files.Count
        };
    }

    public async Task<(ModInfo Mod, ModFileInfo File)?> ResolveInstallFileAsync(
        string projectId,
        string fileId,
        string? gameVersion,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var files = MapFiles(project).ToList();
        var file = string.IsNullOrWhiteSpace(fileId) || fileId == "latest"
            ? ModCompatibilityEvaluator.SelectRecommendedFile(files, gameVersion)
            : files.FirstOrDefault(candidate => candidate.Id == fileId);
        if (file is null ||
            ModCompatibilityEvaluator.Evaluate(gameVersion, file.GameVersions) == ModCompatibilityStatus.Incompatible)
        {
            return null;
        }

        return (MapProject(project, includeVersions: true), file);
    }

    private async Task<Project> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
        => await GetAsync<Project>($"/projects/{Uri.EscapeDataString(projectId)}", cancellationToken);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Hyprism/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("Modifold returned an empty response.");
    }

    private static ModInfo MapProject(Project project, bool includeVersions = false)
    {
        var files = includeVersions ? MapFiles(project).ToList() : [];
        var latest = files.FirstOrDefault();
        return new ModInfo
        {
            Source = "modifold",
            Id = project.Id,
            Name = project.Title ?? project.Slug,
            Slug = project.Slug,
            PageUrl = $"https://modifold.com/mod/{Uri.EscapeDataString(project.Slug)}",
            Summary = project.Summary ?? string.Empty,
            Description = project.Description ?? string.Empty,
            Author = project.Owner?.Username ?? string.Empty,
            AuthorAvatarUrl = project.Owner?.Avatar ?? string.Empty,
            DownloadCount = (int)Math.Min(int.MaxValue, project.Downloads),
            IconUrl = project.IconUrl ?? string.Empty,
            Categories = ReadStringList(project.Tags),
            DateUpdated = project.UpdatedAt ?? string.Empty,
            LatestFileId = latest?.Id ?? "latest",
            LatestFiles = includeVersions
                ? files
                : [new ModFileInfo
                {
                    Source = "modifold",
                    ModId = project.Id,
                    Id = "latest",
                    GameVersions = project.GameVersions ?? []
                }],
            Screenshots = project.Gallery?.Where(image => !string.IsNullOrWhiteSpace(image.Url))
                .Select(image => new CurseForgeScreenshot
                {
                    Url = image.Url,
                    ThumbnailUrl = image.Url
                }).ToList() ?? []
        };
    }

    private static IEnumerable<ModFileInfo> MapFiles(Project project)
    {
        foreach (var version in project.Versions ?? [])
        {
            var url = version.DownloadUrl ?? version.FileUrl ?? version.PrimaryFile?.Url ??
                      version.Files?.FirstOrDefault(file => file.Primary)?.Url ??
                      version.Files?.FirstOrDefault()?.Url;
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var fileName = Uri.TryCreate(url, UriKind.Absolute, out var fileUri)
                ? Path.GetFileName(fileUri.LocalPath)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"{version.VersionNumber ?? version.Id}.jar";

            yield return new ModFileInfo
            {
                Source = "modifold",
                Id = version.Id,
                ModId = project.Id,
                FileName = fileName,
                DisplayName = version.VersionNumber ?? version.Id,
                DownloadUrl = url,
                FileLength = version.FileSize,
                FileDate = version.CreatedAt ?? string.Empty,
                ReleaseType = version.ReleaseChannel?.ToLowerInvariant() switch
                {
                    "beta" => 2,
                    "alpha" => 3,
                    _ => 1
                },
                GameVersions = ReadStringList(version.GameVersions),
                DownloadCount = (int)Math.Min(int.MaxValue, version.Downloads)
            };
        }
    }

    private static List<string> ReadStringList(JsonElement values)
        => values.ValueKind switch
        {
            JsonValueKind.Array => values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString() ?? string.Empty)
                .Where(value => value.Length > 0).ToList(),
            JsonValueKind.String => (values.GetString() ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
            _ => []
        };

    private sealed class ProjectPage
    {
        public List<Project>? Projects { get; set; }
        [JsonPropertyName("totalPages")] public int TotalPages { get; set; }
        [JsonPropertyName("currentPage")] public int CurrentPage { get; set; }
        public Pagination? Pagination { get; set; }
    }

    private sealed class Pagination
    {
        [JsonPropertyName("hasMore")] public bool HasMore { get; set; }
    }

    private sealed class Project
    {
        public string Id { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? ProjectType { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
        public long Downloads { get; set; }
        public string? UpdatedAt { get; set; }
        public JsonElement Tags { get; set; }
        public List<string>? GameVersions { get; set; }
        public Owner? Owner { get; set; }
        public List<GalleryImage>? Gallery { get; set; }
        public List<Version>? Versions { get; set; }
    }

    private sealed class Owner
    {
        public string? Username { get; set; }
        public string? Avatar { get; set; }
    }

    private sealed class GalleryImage
    {
        public string? Url { get; set; }
    }

    private sealed class Version
    {
        public string Id { get; set; } = string.Empty;
        public string? VersionNumber { get; set; }
        public string? DownloadUrl { get; set; }
        public string? FileUrl { get; set; }
        public long FileSize { get; set; }
        public string? CreatedAt { get; set; }
        public string? ReleaseChannel { get; set; }
        public JsonElement GameVersions { get; set; }
        public long Downloads { get; set; }
        public ModifoldFile? PrimaryFile { get; set; }
        public List<ModifoldFile>? Files { get; set; }
    }

    private sealed class ModifoldFile
    {
        public string? Url { get; set; }
        public bool Primary { get; set; }
    }
}
