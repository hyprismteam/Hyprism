// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Application.Progress;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game.Instances;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hyprism.Core.Models;
using System.Text.RegularExpressions;
using System.Net.Http.Json;

namespace Hyprism.Core.Game.Mods;

/// <summary>
/// Manages game modifications including searching, installing, updating, and tracking.
/// Integrates with supported mod catalogs for discovery and downloading.
/// </summary>
public partial class ModManager : IModManager
{
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    private readonly CurseForgeClient _cfClient;
    private readonly ModifoldClient _modifoldClient;

    private static readonly SemaphoreSlim _modManifestLock = new(1, 1);

    /// <summary>
    /// Ensures the <paramref name="modsPath"/> exists as a directory.
    /// The Hytale game sometimes creates a regular <b>file</b> named <c>Mods</c>
    /// inside <c>UserData/</c>.  <see cref="Directory.CreateDirectory(string)"/> throws
    /// <see cref="IOException"/> when a file with the same name already exists,
    /// so we delete the conflicting file first.
    /// </summary>
    private static void EnsureModsDirectory(string modsPath)
    {
        if (File.Exists(modsPath))
        {
            Logger.Warning("ModManager",
                $"Found a file where the Mods directory should be ({modsPath}), removing it");
            File.Delete(modsPath);
        }

        Directory.CreateDirectory(modsPath);
    }

    /// <inheritdoc/>
    public string GetOrCreateModsDirectory(string instancePath)
    {
        var modsPath = Path.Combine(instancePath, "UserData", "Mods");
        EnsureModsDirectory(modsPath);
        return modsPath;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConfigStore _configStore;
    private readonly IInstanceRepository _instances;
    private readonly IProgressReporter _progressNotificationService;

    private sealed class ModDependencyException(string message) : Exception(message);

    /// <summary>
    /// Initializes a new instance of the <see cref="ModManager"/> class.
    /// </summary>
    public ModManager(
        HttpClient httpClient,
        string appDir,
        IConfigStore configStore,
        IInstanceRepository instances,
        IProgressReporter progressNotificationService)
    {
        _httpClient = httpClient;
        _appDir = appDir;
        _configStore = configStore;
        _instances = instances;
        _progressNotificationService = progressNotificationService;
        _cfClient = new CurseForgeClient(httpClient, () => _configStore.Configuration.CurseForgeKey);
        _modifoldClient = new ModifoldClient(httpClient);
    }

    /// <inheritdoc/>
    public Task<ModSearchResult> SearchModsAsync(
        string source, string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
        => source switch
        {
            "curseforge" => SearchModsAsync(query, page, pageSize, categories, sortField, sortOrder),
            "modifold" => _modifoldClient.SearchAsync(query, page, pageSize, sortField,
                categories.FirstOrDefault()),
            _ => Task.FromException<ModSearchResult>(new ArgumentOutOfRangeException(nameof(source)))
        };

    /// <inheritdoc/>
    public Task<ModFilesResult> GetModFilesAsync(string source, string modId, int page, int pageSize)
        => source switch
        {
            "curseforge" => GetModFilesAsync(modId, page, pageSize),
            "modifold" => _modifoldClient.GetFilesAsync(modId, page, pageSize),
            _ => Task.FromException<ModFilesResult>(new ArgumentOutOfRangeException(nameof(source)))
        };

    /// <inheritdoc/>
    public Task<ModInfo?> GetModAsync(string source, string modIdOrSlug)
        => source switch
        {
            "curseforge" => GetModAsync(modIdOrSlug),
            "modifold" => GetModifoldModAsync(modIdOrSlug),
            _ => Task.FromException<ModInfo?>(new ArgumentOutOfRangeException(nameof(source)))
        };

    private async Task<ModInfo?> GetModifoldModAsync(string projectId)
        => await _modifoldClient.GetModAsync(projectId);

    /// <inheritdoc/>
    public Task<List<ModDependency>> GetModDependenciesAsync(string source, string modId, string fileId)
        => source switch
        {
            "curseforge" => GetModDependenciesAsync(modId, fileId),
            "modifold" => Task.FromResult(new List<ModDependency>()),
            _ => Task.FromException<List<ModDependency>>(new ArgumentOutOfRangeException(nameof(source)))
        };

    /// <inheritdoc/>
    public Task<bool> InstallModFileToInstanceAsync(
        string source, string modId, string fileId, string instancePath, Action<string, string>? onProgress = null)
        => source switch
        {
            "curseforge" => InstallModFileToInstanceAsync(modId, fileId, instancePath, onProgress),
            "modifold" => InstallModifoldFileAsync(modId, fileId, instancePath, onProgress),
            _ => Task.FromException<bool>(new ArgumentOutOfRangeException(nameof(source)))
        };

    private async Task<bool> InstallModifoldFileAsync(
        string projectId, string fileId, string instancePath, Action<string, string>? onProgress)
    {
        string? temporaryPath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(projectId) ||
                projectId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
                return false;

            var gameVersion = ModCompatibilityEvaluator.DetectInstanceGameVersion(instancePath);
            var resolved = await _modifoldClient.ResolveInstallFileAsync(projectId, fileId, gameVersion);
            if (resolved is null)
                return false;

            var (mod, file) = resolved.Value;
            if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps)
                return false;

            var extension = Path.GetExtension(file.FileName);
            if (!extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return false;

            var modsPath = GetOrCreateModsDirectory(instancePath);
            var safeName = Path.GetFileName(file.FileName);
            var fileName = $"modifold-{projectId}-{safeName}";
            var filePath = Path.Combine(modsPath, fileName);
            temporaryPath = Path.Combine(modsPath, $".hyprism-{Guid.NewGuid():N}.download");
            onProgress?.Invoke("downloading", fileName);
            using (var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write);
                await response.Content.CopyToAsync(output);
                if (output.Length == 0)
                    return false;
            }

            File.Move(temporaryPath, filePath, true);
            temporaryPath = null;
            onProgress?.Invoke("installing", fileName);
            var manifest = HytaleModManifestReader.Read(filePath);
            var mods = GetInstanceInstalledMods(instancePath);
            mods.RemoveAll(installed => installed.Id == $"local-{fileName}");
            var previous = mods.Where(installed =>
                installed.Id == $"modifold-{projectId}" ||
                (installed.Source == "modifold" && installed.SourceProjectId == projectId)).ToList();
            mods.RemoveAll(installed => previous.Contains(installed));
            mods.Add(new InstalledMod
            {
                Id = $"modifold-{projectId}",
                Source = "modifold",
                SourceProjectId = projectId,
                PageUrl = mod.PageUrl,
                Name = mod.Name,
                Slug = mod.Slug,
                Version = file.DisplayName,
                FileId = file.Id,
                FileName = fileName,
                Enabled = true,
                Author = mod.Author,
                Description = mod.Summary,
                IconUrl = mod.IconUrl,
                FileDate = file.FileDate,
                ReleaseType = file.ReleaseType,
                ManifestId = manifest?.ManifestId ?? string.Empty,
                ManifestVersion = manifest?.Version ?? string.Empty,
                ManifestDependencies = manifest?.Dependencies ?? [],
                ManifestOptionalDependencies = manifest?.OptionalDependencies ?? [],
                ManifestLoadBefore = manifest?.LoadBefore ?? [],
                Screenshots = mod.Screenshots
            });
            await SaveInstanceModsAsync(instancePath, mods);

            foreach (var old in previous)
            {
                if (old.FileName == fileName || string.IsNullOrWhiteSpace(old.FileName))
                    continue;
                var oldPath = Path.Combine(modsPath, Path.GetFileName(old.FileName));
                try
                {
                    if (File.Exists(oldPath))
                        File.Delete(oldPath);
                    if (File.Exists(oldPath + ".disabled"))
                        File.Delete(oldPath + ".disabled");
                }
                catch (IOException ex)
                {
                    Logger.Warning("ModManager", $"Could not remove previous Modifold file: {ex.Message}");
                }
            }

            onProgress?.Invoke("complete", fileName);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Modifold install failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <inheritdoc/>
    public async Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
    {
        if (!_cfClient.HasApiKey())
            return new ModSearchResult { Mods = [], TotalCount = 0 };

        try
        {
            var index = page * pageSize;
            var sortOrderStr = sortOrder == 0 ? "asc" : "desc";
            var endpoint = $"/v1/mods/search?gameId={CurseForgeClient.HytaleGameId}" +
                           $"&searchFilter={Uri.EscapeDataString(query)}" +
                           $"&index={index}&pageSize={pageSize}" +
                           $"&sortField={sortField}&sortOrder={sortOrderStr}";

            if (categories is { Length: > 0 })
            {
                var catId = categories[0];
                if (int.TryParse(catId, out var categoryId) && categoryId > 0)
                    endpoint += $"&categoryId={categoryId}";
            }

            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModManager", $"CurseForge search returned {response.StatusCode}");
                return new ModSearchResult { Mods = [], TotalCount = 0 };
            }

            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(json, _jsonOptions);

            if (cfResponse?.Data == null)
                return new ModSearchResult { Mods = [], TotalCount = 0 };

            var mods = cfResponse.Data.Select(MapToModInfo).ToList();

            return new ModSearchResult
            {
                Mods = mods,
                TotalCount = cfResponse.Pagination?.TotalCount ?? mods.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Search failed: {ex.Message}");
            return new ModSearchResult { Mods = [], TotalCount = 0 };
        }
    }

    /// <inheritdoc/>
    public async Task<List<ModCategory>> GetModCategoriesAsync()
    {
        if (!_cfClient.HasApiKey())
            return GetFallbackCategories();

        try
        {
            var endpoint = $"/v1/categories?gameId={CurseForgeClient.HytaleGameId}";
            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModManager", $"Categories request returned {response.StatusCode}");
                return GetFallbackCategories();
            }

            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeCategoriesResponse>(json, _jsonOptions);

            if (cfResponse?.Data == null || cfResponse.Data.Count == 0)
                return GetFallbackCategories();

            var modsClass = cfResponse.Data.FirstOrDefault(c => c.IsClass == true &&
                string.Equals(c.Name, "mods", StringComparison.OrdinalIgnoreCase));
            int modsClassId = modsClass?.Id ?? 0;

            var categories = new List<ModCategory>
            {
                new() { Id = 0, Name = "All Mods", Slug = "all" }
            };

            var modCategories = cfResponse.Data
                .Where(c => c.ParentCategoryId == modsClassId && c.IsClass != true)
                .Select(c => new ModCategory
                {
                    Id = c.Id,
                    Name = c.Name ?? "",
                    Slug = c.Slug ?? ""
                })
                .OrderBy(c => c.Name)
                .ToList();

            if (modCategories.Count == 0)
            {
                modCategories = [.. cfResponse.Data
                    .Where(c => c.IsClass != true)
                    .Select(c => new ModCategory
                    {
                        Id = c.Id,
                        Name = c.Name ?? "",
                        Slug = c.Slug ?? ""
                    })
                    .OrderBy(c => c.Name)];
            }

            categories.AddRange(modCategories);

            return categories;
        }
        catch (Exception ex)
        {
            Logger.Warning("ModManager", $"Failed to load categories: {ex.Message}");
            return GetFallbackCategories();
        }
    }

    private static List<ModCategory> GetFallbackCategories()
    {
        return
        [
            new() { Id = 0, Name = "All Mods", Slug = "all" },
            // Negative identifiers keep fallback categories visible without pretending
            // that their CurseForge IDs are known. Search ignores these values until
            // the categories endpoint supplies the real identifiers.
            new() { Id = -1, Name = "Blocks", Slug = "blocks" },
            new() { Id = -2, Name = "Cosmetics/Armor", Slug = "cosmetics-armor" },
            new() { Id = -3, Name = "Food/Farming", Slug = "food-farming" },
            new() { Id = -4, Name = "Furniture", Slug = "furniture" },
            new() { Id = -5, Name = "Gameplay", Slug = "gameplay" },
            new() { Id = -6, Name = "Library", Slug = "library" },
            new() { Id = -7, Name = "Miscellaneous", Slug = "miscellaneous" },
            new() { Id = -8, Name = "Mobs/Characters", Slug = "mobs-characters" },
            new() { Id = -9, Name = "Prefab", Slug = "prefab" },
            new() { Id = -10, Name = "Quality of Life", Slug = "quality-of-life" },
            new() { Id = -11, Name = "Resource Packs", Slug = "resource-packs" },
            new() { Id = -12, Name = "Utility", Slug = "utility" },
            new() { Id = -13, Name = "World Gen", Slug = "world-gen" }
        ];
    }

    /// <inheritdoc/>
    public async Task<bool> InstallModFileToInstanceAsync(string slugOrId, string fileIdOrVersion, string instancePath, Action<string, string>? onProgress = null)
    {
        if (!_cfClient.HasApiKey())
            return false;

        try
        {
            var requestedFileId = string.IsNullOrWhiteSpace(fileIdOrVersion) ? null : fileIdOrVersion.Trim();
            var instanceGameVersion = ModCompatibilityEvaluator.DetectInstanceGameVersion(instancePath);
            var rootFile = await _cfClient.ResolveFileAsync(slugOrId, requestedFileId, instanceGameVersion);
            if (rootFile is null)
            {
                Logger.Warning("ModManager", $"File info missing for mod {slugOrId} file '{fileIdOrVersion}'");
                return false;
            }

            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolvedDependencies = new Dictionary<string, CurseForgeFile>(StringComparer.OrdinalIgnoreCase);
            return await InstallDependencyGraphAsync(
                rootFile,
                instancePath,
                instanceGameVersion,
                visiting,
                completed,
                resolvedDependencies,
                onProgress,
                isRoot: true);
        }
        catch (ModDependencyException ex)
        {
            Logger.Warning("ModManager", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Install failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> InstallDependencyGraphAsync(
        CurseForgeFile file,
        string instancePath,
        string? instanceGameVersion,
        HashSet<string> visiting,
        HashSet<string> completed,
        Dictionary<string, CurseForgeFile> resolvedDependencies,
        Action<string, string>? onProgress,
        bool isRoot)
    {
        var fileKey = $"{file.ModId}:{file.Id}";
        if (!visiting.Add(fileKey))
            throw new ModDependencyException($"Circular mod dependency detected at {fileKey}");

        var currentInstalledMods = GetInstanceInstalledMods(instancePath);
        if (isRoot && currentInstalledMods.Any(mod =>
                mod.Enabled &&
                string.Equals(mod.CurseForgeId, file.ModId.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(mod.FileId, file.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            onProgress?.Invoke("complete", file.FileName ?? file.Id.ToString());
            return true;
        }

        foreach (var dependency in file.Dependencies ?? [])
        {
            if (dependency.ModId <= 0)
                continue;

            if (dependency.RelationType == CurseForgeDependencyRelationType.Incompatible)
            {
                if (FindInstalledMod(currentInstalledMods, dependency.ModId.ToString()) is not null)
                    throw new ModDependencyException(
                        $"Mod {file.ModId} is incompatible with CurseForge mod {dependency.ModId}");
                continue;
            }

            if (dependency.RelationType != CurseForgeDependencyRelationType.RequiredDependency ||
                IsDependencySatisfied(currentInstalledMods, dependency))
            {
                continue;
            }

            var dependencyKey = $"{dependency.ModId}:{dependency.FileId}";
            if (!resolvedDependencies.TryGetValue(dependencyKey, out var dependencyFile))
            {
                dependencyFile = await _cfClient.ResolveFileAsync(
                    dependency.ModId.ToString(),
                    dependency.FileId > 0 ? dependency.FileId.ToString() : null,
                    instanceGameVersion) ?? throw new ModDependencyException(
                        $"Required dependency {dependency.ModId} could not be resolved");
                resolvedDependencies[dependencyKey] = dependencyFile;
            }

            onProgress?.Invoke("dependency", dependencyFile.FileName ?? dependency.ModId.ToString());
            if (!await InstallDependencyGraphAsync(
                    dependencyFile,
                    instancePath,
                    instanceGameVersion,
                    visiting,
                    completed,
                    resolvedDependencies,
                    onProgress,
                    isRoot: false))
            {
                return false;
            }
        }

        visiting.Remove(fileKey);
        if (!completed.Add(fileKey) && !isRoot)
            return true;

        return await InstallRawModFileAsync(file, instancePath, onProgress);
    }

    private async Task<bool> InstallRawModFileAsync(CurseForgeFile cfFile, string instancePath, Action<string, string>? onProgress = null)
    {
        if (!_cfClient.HasApiKey()) return false;

        try
        {
            var numericModId = cfFile.ModId.ToString();
            var resolvedFileId = cfFile.Id > 0 ? cfFile.Id.ToString() : "";

            if (cfFile.ModId <= 0 || string.IsNullOrWhiteSpace(resolvedFileId))
            {
                Logger.Warning("ModManager", "CurseForge returned an incomplete mod file record");
                return false;
            }

            var downloadUrl = await _cfClient.ResolveDownloadUrlAsync(numericModId, resolvedFileId, cfFile.DownloadUrl, cfFile.FileName);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Logger.Warning("ModManager", $"File info missing or no download URL for mod {numericModId} file {resolvedFileId}");
                return false;
            }

            var fileName = string.IsNullOrWhiteSpace(cfFile.FileName)
                ? $"mod_{resolvedFileId}.jar"
                : Path.GetFileName(cfFile.FileName);
            onProgress?.Invoke("downloading", fileName);

            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);

            var filePath = Path.Combine(modsPath, fileName);

            const int maxDownloadAttempts = 3;
            var downloaded = false;
            for (var attempt = 1; attempt <= maxDownloadAttempts; attempt++)
            {
                using var downloadResponse = await _httpClient.GetAsync(downloadUrl);
                if (downloadResponse.IsSuccessStatusCode)
                {
                    await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                    await downloadResponse.Content.CopyToAsync(fs);
                    downloaded = true;
                    break;
                }

                Logger.Warning("ModManager", $"Download returned {downloadResponse.StatusCode} for mod {numericModId} file {resolvedFileId} (attempt {attempt}/{maxDownloadAttempts})");
                if (attempt < maxDownloadAttempts)
                {
                    await Task.Delay(300 * attempt);
                }
            }

            if (!downloaded)
            {
                return false;
            }

            onProgress?.Invoke("installing", fileName);

            CurseForgeMod? modInfo = null;
            try
            {
                var modEndpoint = $"/v1/mods/{numericModId}";
                using var modRequest = _cfClient.CreateRequest(HttpMethod.Get, modEndpoint);
                using var modResponse = await _httpClient.SendAsync(modRequest);
                if (modResponse.IsSuccessStatusCode)
                {
                    var modJson = await modResponse.Content.ReadAsStringAsync();
                    var modResp = JsonSerializer.Deserialize<CurseForgeModResponse>(modJson, _jsonOptions);
                    modInfo = modResp?.Data;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("ModManager", $"Could not load CurseForge metadata for {numericModId}: {ex.Message}");
            }

            var mods = GetInstanceInstalledMods(instancePath);

            var oldMods = mods.Where(m =>
                m.CurseForgeId == numericModId ||
                m.Id == $"cf-{numericModId}").ToList();

            foreach (var oldMod in oldMods)
            {
                if (!string.IsNullOrWhiteSpace(oldMod.FileName) &&
                    !oldMod.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    var oldFilePath = Path.Combine(modsPath, oldMod.FileName);
                    var oldDisabledFilePath = Path.Combine(modsPath, oldMod.FileName + ".disabled");

                    if (File.Exists(oldFilePath))
                    {
                        try
                        {
                            File.Delete(oldFilePath);
                            Logger.Info("ModManager", $"Deleted old mod file: {oldMod.FileName}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("ModManager", $"Failed to delete old mod file {oldMod.FileName}: {ex.Message}");
                        }
                    }
                    else if (File.Exists(oldDisabledFilePath))
                    {
                        try
                        {
                            File.Delete(oldDisabledFilePath);
                            Logger.Info("ModManager", $"Deleted old disabled mod file: {oldMod.FileName}.disabled");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("ModManager", $"Failed to delete old disabled mod file {oldMod.FileName}.disabled: {ex.Message}");
                        }
                    }
                }
            }

            mods.RemoveAll(m => m.CurseForgeId == numericModId || m.Id == $"cf-{numericModId}");

            var manifest = HytaleModManifestReader.Read(filePath);

            var installedMod = new InstalledMod
            {
                Id = $"cf-{numericModId}",
                Name = modInfo?.Name ?? cfFile.DisplayName ?? fileName,
                Slug = modInfo?.Slug ?? "",
                Version = ExtractVersion(cfFile.DisplayName, fileName),
                FileId = resolvedFileId,
                FileName = fileName,
                Enabled = true,
                Author = modInfo?.Authors?.FirstOrDefault()?.Name ?? "",
                Description = modInfo?.Summary ?? "",
                IconUrl = modInfo?.Logo?.ThumbnailUrl ?? "",
                CurseForgeId = numericModId,
                FileDate = cfFile.FileDate ?? "",
                ReleaseType = cfFile.ReleaseType,
                Dependencies = MapDependencies(cfFile.Dependencies),
                ManifestId = manifest?.ManifestId ?? "",
                ManifestVersion = manifest?.Version ?? "",
                ManifestDependencies = manifest?.Dependencies ?? [],
                ManifestOptionalDependencies = manifest?.OptionalDependencies ?? [],
                ManifestLoadBefore = manifest?.LoadBefore ?? [],
                Screenshots = modInfo?.Screenshots?.Select(s => new CurseForgeScreenshot
                {
                    Id = s.Id,
                    Title = s.Title,
                    ThumbnailUrl = s.ThumbnailUrl,
                    Url = s.Url
                }).ToList() ?? []
            };

            mods.Add(installedMod);
            await SaveInstanceModsAsync(instancePath, mods);

            onProgress?.Invoke("complete", fileName);
            Logger.Success("ModManager", $"Installed mod {installedMod.Name} (ID: {numericModId}) to {instancePath}");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Install failed: {ex.Message}");
            return false;
        }
    }

    private static List<ModDependency> MapDependencies(IEnumerable<CurseForgeFileDependency>? dependencies)
        => dependencies?.Where(dependency => dependency.ModId > 0)
            .Select(dependency => new ModDependency
            {
                ModId = dependency.ModId.ToString(),
                FileId = dependency.FileId > 0 ? dependency.FileId.ToString() : "",
                RelationType = dependency.RelationType
            })
            .ToList() ?? [];

    private static InstalledMod? FindInstalledMod(
        IReadOnlyList<InstalledMod> installedMods,
        string curseForgeId)
        => installedMods.FirstOrDefault(mod =>
            mod.Enabled && string.Equals(mod.CurseForgeId, curseForgeId, StringComparison.OrdinalIgnoreCase));

    private static bool IsDependencySatisfied(
        IReadOnlyList<InstalledMod> installedMods,
        CurseForgeFileDependency dependency)
    {
        var installed = FindInstalledMod(installedMods, dependency.ModId.ToString());
        if (installed is null)
            return false;
        return dependency.FileId <= 0 ||
               string.Equals(installed.FileId, dependency.FileId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public List<InstalledMod> GetInstanceInstalledMods(string instancePath)
    {
        var modsPath = Path.Combine(instancePath, "UserData", "Mods");
        var manifestPath = LauncherJsonFile.GetPath(modsPath, "Manifest.json", "manifest.json");
        var legacyManifestPath = Path.Combine(instancePath, "Client", "mods", "manifest.json");

        EnsureModsDirectory(modsPath);

        List<InstalledMod> mods;

        try
        {
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, JsonDefaults.CaseInsensitive) ?? [];
            }
            else if (File.Exists(legacyManifestPath))
            {
                var json = File.ReadAllText(legacyManifestPath);
                mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, JsonDefaults.CaseInsensitive) ?? [];
            }
            else
            {
                mods = [];
            }
        }
        catch
        {
            mods = [];
        }

        var diskFiles = Directory.EnumerateFiles(modsPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Where(name => !name.Equals("Manifest.json", StringComparison.OrdinalIgnoreCase))
            .Where(name =>
                name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.FileName))
                continue;

            if (diskFiles.Any(f => string.Equals(f, mod.FileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var baseStem = mod.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(mod.FileName)
                : Path.GetFileNameWithoutExtension(mod.FileName);

            var candidate = diskFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), baseStem, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)), baseStem, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(candidate))
            {
                mod.FileName = candidate;
                mod.Enabled = !candidate.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (!mod.Enabled && string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension))
                {
                    var stem = Path.GetFileNameWithoutExtension(candidate);
                    var ext = Path.GetExtension(stem).ToLowerInvariant();
                    if (ext is ".jar" or ".zip")
                    {
                        mod.DisabledOriginalExtension = ext;
                    }
                }
            }
        }

        foreach (var mod in mods)
        {
            if (!string.IsNullOrWhiteSpace(mod.FileName) &&
                diskFiles.Any(f => string.Equals(f, mod.FileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var modNameKey = NormalizeName(mod.Name);
            var slugKey = NormalizeName(mod.Slug);

            var candidate = diskFiles.FirstOrDefault(file =>
            {
                var stem = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file))
                    : Path.GetFileNameWithoutExtension(file);

                var fileKey = NormalizeName(stem);
                if (string.IsNullOrEmpty(fileKey)) return false;

                var nameMatch = !string.IsNullOrEmpty(modNameKey) &&
                                (fileKey.StartsWith(modNameKey) || modNameKey.StartsWith(fileKey));
                var slugMatch = !string.IsNullOrEmpty(slugKey) &&
                                (fileKey.Contains(slugKey) || slugKey.Contains(fileKey));

                return nameMatch || slugMatch;
            });

            if (!string.IsNullOrEmpty(candidate))
            {
                mod.FileName = candidate;
                mod.Enabled = !candidate.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (!mod.Enabled && string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension))
                {
                    var stem = Path.GetFileNameWithoutExtension(candidate);
                    var ext = Path.GetExtension(stem).ToLowerInvariant();
                    if (ext is ".jar" or ".zip")
                    {
                        mod.DisabledOriginalExtension = ext;
                    }
                }
            }
        }

        foreach (var fileName in diskFiles)
        {
            if (mods.Any(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var enabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled
                ? Path.GetFileNameWithoutExtension(fileName)
                : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));

            var disabledOriginalExtension = "";
            if (!enabled)
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(stem).ToLowerInvariant();
                if (ext is ".jar" or ".zip")
                {
                    disabledOriginalExtension = ext;
                }
            }

            mods.Add(new InstalledMod
            {
                Id = $"local-{fileName}",
                Name = displayName,
                FileName = fileName,
                Enabled = enabled,
                Version = "local",
                Author = "Local file",
                DisabledOriginalExtension = disabledOriginalExtension
            });
        }

        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.FileName) ||
                !string.IsNullOrWhiteSpace(mod.ManifestId))
            {
                continue;
            }

            var manifest = HytaleModManifestReader.Read(Path.Combine(modsPath, mod.FileName));
            if (manifest is null)
                continue;

            mod.ManifestId = manifest.ManifestId;
            mod.ManifestVersion = manifest.Version;
            mod.ManifestDependencies = manifest.Dependencies;
            mod.ManifestOptionalDependencies = manifest.OptionalDependencies;
            mod.ManifestLoadBefore = manifest.LoadBefore;
        }

        static bool IsSyntheticLocal(InstalledMod mod)
        {
            var isLocalId = mod.Id?.StartsWith("local-", StringComparison.OrdinalIgnoreCase) == true;
            var isLocalVersion = string.Equals(mod.Version, "local", StringComparison.OrdinalIgnoreCase);
            var isLocalAuthor = string.Equals(mod.Author, "Local file", StringComparison.OrdinalIgnoreCase);
            return isLocalId || (isLocalVersion && isLocalAuthor);
        }

        var metadataMods = mods.Where(m => !IsSyntheticLocal(m)).ToList();
        mods = [.. mods
            .Where(local =>
            {
                if (!IsSyntheticLocal(local)) return true;

                var localNameKey = NormalizeName(local.Name);
                var localFileStem = local.FileName?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true
                    ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(local.FileName))
                    : Path.GetFileNameWithoutExtension(local.FileName ?? "");
                var localFileKey = NormalizeName(localFileStem ?? "");

                var hasMetadataTwin = metadataMods.Any(meta =>
                {
                    var metaNameKey = NormalizeName(meta.Name);
                    var metaSlugKey = NormalizeName(meta.Slug);
                    var metaFileStem = meta.FileName?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true
                        ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta.FileName))
                        : Path.GetFileNameWithoutExtension(meta.FileName ?? "");
                    var metaFileKey = NormalizeName(metaFileStem ?? "");

                    if (!string.IsNullOrEmpty(local.FileName) && !string.IsNullOrEmpty(meta.FileName) &&
                        string.Equals(local.FileName, meta.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(localNameKey) &&
                        ((!string.IsNullOrEmpty(metaNameKey) && localNameKey == metaNameKey) ||
                         (!string.IsNullOrEmpty(metaSlugKey) && localNameKey == metaSlugKey)))
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(localFileKey) &&
                        ((!string.IsNullOrEmpty(metaFileKey) && localFileKey == metaFileKey) ||
                         (!string.IsNullOrEmpty(metaNameKey) && (localFileKey.Contains(metaNameKey) || metaNameKey.Contains(localFileKey))) ||
                         (!string.IsNullOrEmpty(metaSlugKey) && (localFileKey.Contains(metaSlugKey) || metaSlugKey.Contains(localFileKey)))))
                    {
                        return true;
                    }

                    return false;
                });

                return !hasMetadataTwin;
            })];

        return mods;
    }

    /// <inheritdoc/>
    public async Task SaveInstanceModsAsync(string instancePath, List<InstalledMod> mods)
    {
        await _modManifestLock.WaitAsync();
        try
        {
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);
            var manifestPath = LauncherJsonFile.GetPath(modsPath, "Manifest.json", "manifest.json");

            var json = JsonSerializer.Serialize(mods, JsonDefaults.Indented);
            await File.WriteAllTextAsync(manifestPath, json);
        }
        finally
        {
            _modManifestLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize)
    {
        if (!_cfClient.HasApiKey())
            return new ModFilesResult();

        try
        {
            var index = page * pageSize;
            var endpoint = $"/v1/mods/{modId}/files?index={index}&pageSize={pageSize}";
            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModManager", $"Get mod files returned {response.StatusCode}");
                return new ModFilesResult();
            }

            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);

            if (cfResponse?.Data == null)
                return new ModFilesResult();

            return new ModFilesResult
            {
                Files = [.. cfResponse.Data.Select(MapToModFileInfo)],
                TotalCount = cfResponse.Pagination?.TotalCount ?? cfResponse.Data.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Get mod files failed: {ex.Message}");
            return new ModFilesResult();
        }
    }

    /// <inheritdoc/>
    public async Task<List<ModDependency>> GetModDependenciesAsync(string modId, string fileId)
    {
        if (!_cfClient.HasApiKey() || string.IsNullOrWhiteSpace(modId))
            return [];

        try
        {
            var sourceFile = await _cfClient.ResolveFileAsync(modId, fileId);
            var dependencies = MapDependencies(sourceFile?.Dependencies)
                .Where(dependency => dependency.RelationType == CurseForgeDependencyRelationType.RequiredDependency)
                .GroupBy(dependency => dependency.ModId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            await Parallel.ForEachAsync(
                dependencies,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (dependency, _) =>
                {
                    try
                    {
                        var modTask = GetModAsync(dependency.ModId);
                        var fileTask = string.IsNullOrWhiteSpace(dependency.FileId)
                            ? Task.FromResult<CurseForgeFile?>(null)
                            : _cfClient.ResolveFileAsync(dependency.ModId, dependency.FileId);
                        await Task.WhenAll(modTask, fileTask);

                        var mod = await modTask;
                        var dependencyFile = await fileTask;
                        var fallbackFile = mod?.LatestFiles?.FirstOrDefault();
                        dependency.Name = string.IsNullOrWhiteSpace(mod?.Name)
                            ? dependency.ModId
                            : mod.Name;
                        dependency.IconUrl = mod?.IconUrl ?? string.Empty;
                        dependency.Version = dependencyFile is not null
                            ? ExtractVersion(dependencyFile.DisplayName, dependencyFile.FileName)
                            : ExtractVersion(fallbackFile?.DisplayName, fallbackFile?.FileName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(
                            "ModManager",
                            $"Could not resolve dependency metadata for {dependency.ModId}: {ex.Message}");
                        dependency.Name = string.IsNullOrWhiteSpace(dependency.Name)
                            ? dependency.ModId
                            : dependency.Name;
                    }
                });

            return dependencies;
        }
        catch (Exception ex)
        {
            Logger.Warning("ModManager", $"Get mod dependencies failed for '{modId}': {ex.Message}");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<ModInfo?> GetModAsync(string modIdOrSlug)
    {
        if (!_cfClient.HasApiKey() || string.IsNullOrWhiteSpace(modIdOrSlug))
            return null;

        try
        {
            CurseForgeMod? cfMod = null;
            var token = modIdOrSlug.Trim();

            if (int.TryParse(token, out _))
            {
                var endpoint = $"/v1/mods/{token}";
                using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var modResponse = JsonSerializer.Deserialize<CurseForgeModResponse>(json, _jsonOptions);
                    cfMod = modResponse?.Data;
                }
            }
            else
            {
                var slugEndpoint = $"/v1/mods/search?gameId={CurseForgeClient.HytaleGameId}&slug={Uri.EscapeDataString(token)}&index=0&pageSize=1";
                using var slugRequest = _cfClient.CreateRequest(HttpMethod.Get, slugEndpoint);
                using var slugResponse = await _httpClient.SendAsync(slugRequest);
                if (slugResponse.IsSuccessStatusCode)
                {
                    var slugJson = await slugResponse.Content.ReadAsStringAsync();
                    var searchResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(slugJson, _jsonOptions);
                    cfMod = searchResponse?.Data?.FirstOrDefault();
                }

                if (cfMod == null)
                {
                    var fallbackEndpoint = $"/v1/mods/search?gameId={CurseForgeClient.HytaleGameId}&searchFilter={Uri.EscapeDataString(token)}&index=0&pageSize=20";
                    using var fallbackRequest = _cfClient.CreateRequest(HttpMethod.Get, fallbackEndpoint);
                    using var fallbackResponse = await _httpClient.SendAsync(fallbackRequest);
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        var fallbackJson = await fallbackResponse.Content.ReadAsStringAsync();
                        var fallbackSearch = JsonSerializer.Deserialize<CurseForgeSearchResponse>(fallbackJson, _jsonOptions);
                        cfMod = fallbackSearch?.Data?.FirstOrDefault(m =>
                            string.Equals(m.Slug, token, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(m.Name, token, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            return cfMod == null ? null : MapToModInfo(cfMod);
        }
        catch (Exception ex)
        {
            Logger.Warning("ModManager", $"Get mod failed for '{modIdOrSlug}': {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string> GetModFileChangelogAsync(string modId, string fileId)
    {
        if (!_cfClient.HasApiKey() || string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(fileId))
            return string.Empty;

        try
        {
            var endpoint = $"/v1/mods/{modId}/files/{fileId}/changelog";
            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModManager", $"Get changelog returned {response.StatusCode} for mod={modId} file={fileId}");
                return string.Empty;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("data", out var dataElement) &&
                    dataElement.ValueKind == JsonValueKind.String)
                {
                    return dataElement.GetString() ?? string.Empty;
                }

                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    return doc.RootElement.GetString() ?? string.Empty;
                }
            }

            return body;
        }
        catch (Exception ex)
        {
            Logger.Warning("ModManager", $"Get changelog failed for mod={modId} file={fileId}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string instancePath)
    {
        if (!_cfClient.HasApiKey())
            return [];

        var installedMods = GetInstanceInstalledMods(instancePath);
        var modsWithUpdates = new List<InstalledMod>();

        foreach (var mod in installedMods)
        {
            if (string.IsNullOrEmpty(mod.CurseForgeId)) continue;

            try
            {
                var endpoint = $"/v1/mods/{mod.CurseForgeId}/files?pageSize=1";
                using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync();
                var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);

                var latestFile = cfResponse?.Data?.FirstOrDefault();
                if (latestFile == null) continue;

                if (!string.IsNullOrEmpty(mod.FileId) && latestFile.Id.ToString() != mod.FileId)
                {
                    mod.LatestFileId = latestFile.Id.ToString();
                    mod.LatestVersion = latestFile.DisplayName ?? "";
                    modsWithUpdates.Add(mod);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("ModManager", $"Update check failed for {mod.Name}: {ex.Message}");
            }
        }

        return modsWithUpdates;
    }

    /// <inheritdoc/>
    public async Task<bool> InstallLocalModFile(string sourcePath, string instancePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                Logger.Warning("ModManager", $"Source mod file not found: {sourcePath}");
                return false;
            }

            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);

            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(modsPath, fileName);

            File.Copy(sourcePath, destPath, true);

            var mods = GetInstanceInstalledMods(instancePath);

            mods.RemoveAll(m => m.FileName == fileName);

            mods.Add(new InstalledMod
            {
                Id = $"local-{Guid.NewGuid():N}",
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Enabled = true,
                Version = "local",
                Author = "Local file"
            });

            await SaveInstanceModsAsync(instancePath, mods);
            Logger.Success("ModManager", $"Installed local mod: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Install local mod failed: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> InstallModFromBase64(string fileName, string base64Content, string instancePath)
    {
        try
        {
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);

            var destPath = Path.Combine(modsPath, fileName);
            var bytes = Convert.FromBase64String(base64Content);
            await File.WriteAllBytesAsync(destPath, bytes);

            var mods = GetInstanceInstalledMods(instancePath);
            mods.RemoveAll(m => m.FileName == fileName);

            mods.Add(new InstalledMod
            {
                Id = $"local-{Guid.NewGuid():N}",
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Enabled = true,
                Version = "local",
                Author = "Imported file"
            });

            await SaveInstanceModsAsync(instancePath, mods);
            Logger.Success("ModManager", $"Installed mod from base64: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Install mod from base64 failed: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetModEnabledAsync(string instancePath, string modId, bool enabled)
    {
        var modsPath = Path.Combine(instancePath, "UserData", "Mods");
        var mods = GetInstanceInstalledMods(instancePath);
        var mod = mods.FirstOrDefault(m =>
            string.Equals(m.Id, modId, StringComparison.Ordinal) ||
            string.Equals(m.Name, modId, StringComparison.Ordinal));
        if (mod is null || string.IsNullOrEmpty(mod.FileName))
            return false;

        if (mod.Enabled == enabled)
            return true;

        var currentPath = Path.Combine(modsPath, mod.FileName);
        if (!File.Exists(currentPath))
        {
            var stem = Path.GetFileNameWithoutExtension(mod.FileName);
            var probes = new[]
            {
                currentPath,
                Path.Combine(modsPath, $"{stem}.jar"),
                Path.Combine(modsPath, $"{stem}.zip"),
                Path.Combine(modsPath, $"{stem}.disabled"),
                Path.Combine(modsPath, $"{stem}.jar.disabled"),
                Path.Combine(modsPath, $"{stem}.zip.disabled"),
            };
            var found = probes.FirstOrDefault(File.Exists);
            if (found is null)
                return false;
            currentPath = found;
            mod.FileName = Path.GetFileName(found);
            mod.Enabled = !found.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            if (mod.Enabled == enabled)
                return true;
        }

        try
        {
            if (!enabled)
            {
                var fileName = Path.GetFileName(currentPath);
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (extension is ".jar" or ".zip")
                    mod.DisabledOriginalExtension = extension;
                var disabledName = $"{Path.GetFileNameWithoutExtension(fileName)}.disabled";
                File.Move(currentPath, Path.Combine(modsPath, disabledName), true);
                mod.FileName = disabledName;
                mod.Enabled = false;
            }
            else
            {
                var fileName = Path.GetFileName(currentPath);
                var stem = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^".disabled".Length]
                    : Path.GetFileNameWithoutExtension(fileName);
                var restoreExtension = !string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension)
                    ? (mod.DisabledOriginalExtension.StartsWith('.')
                        ? mod.DisabledOriginalExtension
                        : $".{mod.DisabledOriginalExtension}")
                    : (stem.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                       stem.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? ""
                        : ".jar");
                var enabledName = string.IsNullOrEmpty(restoreExtension) ? stem : $"{stem}{restoreExtension}";
                File.Move(currentPath, Path.Combine(modsPath, enabledName), true);
                mod.FileName = enabledName;
                mod.Enabled = true;
                mod.DisabledOriginalExtension = "";
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ModManager", $"Toggle mod '{modId}' failed: {ex.Message}");
            return false;
        }

        await SaveInstanceModsAsync(instancePath, mods);
        Logger.Info("ModManager", $"Mod '{mod.Name}' {(enabled ? "enabled" : "disabled")}");
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveInstalledModAsync(string instancePath, string modId)
    {
        var mods = GetInstanceInstalledMods(instancePath);
        var mod = mods.FirstOrDefault(m =>
            string.Equals(m.Id, modId, StringComparison.Ordinal) ||
            string.Equals(m.Name, modId, StringComparison.Ordinal));
        if (mod is null)
            return false;

        var dependents = mods.Where(candidate =>
                !ReferenceEquals(candidate, mod) &&
                DependsOn(candidate, mod))
            .Select(candidate => candidate.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (dependents.Count > 0)
        {
            Logger.Warning(
                "ModManager",
                $"Cannot remove '{mod.Name}' because it is required by: {string.Join(", ", dependents)}");
            return false;
        }

        mods.Remove(mod);

        if (!string.IsNullOrEmpty(mod.FileName))
        {
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            if (!TryDeleteModFile(modsPath, mod.FileName))
                Logger.Warning("ModManager", $"Could not find mod file to delete: {mod.FileName}");
        }

        await SaveInstanceModsAsync(instancePath, mods);
        Logger.Info("ModManager", $"Removed mod '{mod.Name}'");
        return true;
    }

    private static bool DependsOn(InstalledMod candidate, InstalledMod dependency)
    {
        var curseForgeMatch = !string.IsNullOrWhiteSpace(dependency.CurseForgeId) &&
            candidate.Dependencies.Any(relation =>
                relation.RelationType == CurseForgeDependencyRelationType.RequiredDependency &&
                string.Equals(relation.ModId, dependency.CurseForgeId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(relation.FileId) ||
                 string.IsNullOrWhiteSpace(dependency.FileId) ||
                 string.Equals(relation.FileId, dependency.FileId, StringComparison.OrdinalIgnoreCase)));
        if (curseForgeMatch)
            return true;

        return !string.IsNullOrWhiteSpace(dependency.ManifestId) &&
            candidate.ManifestDependencies.Any(relation =>
                string.Equals(relation.Id, dependency.ManifestId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryDeleteModFile(string modsDir, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(modsDir, fileName),
            Path.Combine(modsDir, fileName + ".disabled"),
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;
            try
            {
                File.Delete(candidate);
                return true;
            }
            catch
            {
                // A missing file is acceptable; the manifest entry is still removed
            }
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            stem.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            stem = Path.GetFileNameWithoutExtension(stem);

        if (!Directory.Exists(modsDir))
            return false;

        try
        {
            foreach (var path in Directory.GetFiles(modsDir))
            {
                var name = Path.GetFileName(path).ToLowerInvariant();
                var normalizedStem = stem.ToLowerInvariant();
                if (name == $"{normalizedStem}.jar" ||
                    name == $"{normalizedStem}.jar.disabled" ||
                    name == $"{normalizedStem}.zip" ||
                    name == $"{normalizedStem}.zip.disabled" ||
                    name == $"{normalizedStem}.disabled")
                {
                    try
                    {
                        File.Delete(path);
                        return true;
                    }
                    catch
                    {
                        // Keep probing other candidates
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Tries to extract a semver-like version string from a display name or filename.
    /// Looks for semver-like patterns (e.g., "1.2.7", "0.3.1-beta") and returns the first match.
    /// Falls back to DisplayName or FileName if no version pattern is found.
    /// </summary>
    private static string ExtractVersion(string? displayName, string? fileName)
    {
        var versionRegex = SemanticVersionRegex();

        if (!string.IsNullOrEmpty(displayName))
        {
            var match = versionRegex.Match(displayName);
            if (match.Success) return match.Groups[1].Value;
        }

        if (!string.IsNullOrEmpty(fileName))
        {
            var name = fileName;
            if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            var match = versionRegex.Match(name);
            if (match.Success) return match.Groups[1].Value;
        }

        return displayName ?? fileName ?? "";
    }

    /// <summary>
    /// Maps a CurseForge API mod to the normalized ModInfo.
    /// </summary>
    private static ModInfo MapToModInfo(CurseForgeMod cfMod)
    {
        var author = cfMod.Authors?.FirstOrDefault();
        return new ModInfo
        {
            Id = cfMod.Id.ToString(),
            Name = cfMod.Name ?? "",
            Slug = cfMod.Slug ?? "",
            Summary = cfMod.Summary ?? "",
            Author = author?.Name ?? "",
            AuthorAvatarUrl = author?.AvatarUrl ?? "",
            DownloadCount = cfMod.DownloadCount,
            IconUrl = cfMod.Logo?.ThumbnailUrl ?? "",
            ThumbnailUrl = cfMod.Logo?.Url ?? "",
            Categories = cfMod.Categories?.Select(c => c.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? [],
            DateUpdated = cfMod.DateModified ?? "",
            LatestFileId = cfMod.LatestFiles?.FirstOrDefault()?.Id.ToString() ?? "",
            LatestFiles = cfMod.LatestFiles?.Select(MapToModFileInfo).ToList() ?? [],
            Screenshots = cfMod.Screenshots ?? []
        };
    }

    private static ModFileInfo MapToModFileInfo(CurseForgeFile file) => new()
    {
        Id = file.Id.ToString(),
        ModId = file.ModId.ToString(),
        FileName = file.FileName ?? "",
        DisplayName = file.DisplayName ?? "",
        DownloadUrl = file.DownloadUrl ?? "",
        FileLength = file.FileLength,
        FileDate = file.FileDate ?? "",
        ReleaseType = file.ReleaseType,
        GameVersions = file.GameVersions ?? [],
        DownloadCount = file.DownloadCount,
        Dependencies = MapDependencies(file.Dependencies)
    };

    [GeneratedRegex(@"(\d+\.\d+(?:\.\d+)?(?:[-.]\w+)*)")]
    private static partial Regex SemanticVersionRegex();
}
