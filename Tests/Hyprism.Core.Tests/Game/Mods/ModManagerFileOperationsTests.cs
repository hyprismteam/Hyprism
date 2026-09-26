// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;
using Moq;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Hyprism.Core.Tests.Game.Mods;

public class ModManagerFileOperationsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancePath;
    private readonly string _modsPath;
    private readonly ModManager _manager;

    public ModManagerFileOperationsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyprismModManagerTests_" + Guid.NewGuid());
        _instancePath = Path.Combine(_tempDir, "instance");
        _modsPath = Path.Combine(_instancePath, "UserData", "Mods");
        Directory.CreateDirectory(_modsPath);

        _manager = new ModManager(
            new HttpClient(),
            _tempDir,
            new JsonConfigStore(_tempDir),
            new Mock<IInstanceRepository>().Object,
            new Mock<IProgressReporter>().Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SetModEnabledAsync_DisablesAndReEnablesTheModFile()
    {
        await WriteInstalledModAsync("example-mod", "example-mod-1.0.jar");

        Assert.True(await _manager.SetModEnabledAsync(_instancePath, "example-mod", false));
        Assert.True(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.disabled")));
        Assert.False(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.jar")));
        var disabled = Assert.Single(_manager.GetInstanceInstalledMods(_instancePath));
        Assert.False(disabled.Enabled);
        Assert.Equal(".jar", disabled.DisabledOriginalExtension);

        Assert.True(await _manager.SetModEnabledAsync(_instancePath, "example-mod", true));
        Assert.True(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.jar")));
        Assert.False(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.disabled")));
        var enabled = Assert.Single(_manager.GetInstanceInstalledMods(_instancePath));
        Assert.True(enabled.Enabled);
    }

    [Fact]
    public async Task SetModEnabledAsync_ReturnsFalseForUnknownMod()
    {
        await WriteInstalledModAsync("known-mod", "known-mod-1.0.jar");

        Assert.False(await _manager.SetModEnabledAsync(_instancePath, "missing-mod", false));
    }

    [Fact]
    public async Task SearchModsAsync_MapsAuthorAvatarFromCurseForgeResponse()
    {
        const string avatarUrl =
            "https://media.forgecdn.net/avatars/1625/902/639044029153803750.jpeg";
        var configStore = new JsonConfigStore(_tempDir);
        configStore.Configuration.CurseForgeKey = "test-key";
        using var httpClient = new HttpClient(new StaticJsonHandler($$"""
            {
              "data": [
                {
                  "id": 1430352,
                  "name": "BetterMap",
                  "authors": [
                    {
                      "id": 136575006,
                      "name": "Paralaxe",
                      "url": "https://www.curseforge.com/members/paralaxe",
                      "avatarUrl": "{{avatarUrl}}"
                    }
                  ]
                }
              ],
              "pagination": { "totalCount": 1 }
            }
            """));
        var manager = new ModManager(
            httpClient,
            _tempDir,
            configStore,
            new Mock<IInstanceRepository>().Object,
            new Mock<IProgressReporter>().Object);

        var result = await manager.SearchModsAsync("BetterMap", 0, 1, [], 2, 1);

        var mod = Assert.Single(result.Mods);
        Assert.Equal("Paralaxe", mod.Author);
        Assert.Equal(avatarUrl, mod.AuthorAvatarUrl);
    }

    [Fact]
    public async Task GetModCategoriesAsync_ReturnsAllFallbackCategoriesWithoutAnApiKey()
    {
        var categories = await _manager.GetModCategoriesAsync();

        Assert.Equal(
            [
                "all",
                "blocks",
                "cosmetics-armor",
                "food-farming",
                "furniture",
                "gameplay",
                "library",
                "miscellaneous",
                "mobs-characters",
                "prefab",
                "quality-of-life",
                "resource-packs",
                "utility",
                "world-gen"
            ],
            categories.Select(category => category.Slug));
    }

    [Fact]
    public async Task RemoveInstalledModAsync_DeletesFileAndManifestEntry()
    {
        await WriteInstalledModAsync("doomed-mod", "doomed-mod-1.0.jar");

        Assert.True(await _manager.RemoveInstalledModAsync(_instancePath, "doomed-mod"));
        Assert.False(File.Exists(Path.Combine(_modsPath, "doomed-mod-1.0.jar")));
        Assert.Empty(_manager.GetInstanceInstalledMods(_instancePath));
    }

    [Fact]
    public async Task RemoveInstalledModAsync_DeletesDisabledFile()
    {
        await WriteInstalledModAsync("disabled-mod", "disabled-mod-1.0.jar");
        Assert.True(await _manager.SetModEnabledAsync(_instancePath, "disabled-mod", false));

        Assert.True(await _manager.RemoveInstalledModAsync(_instancePath, "disabled-mod"));
        Assert.False(File.Exists(Path.Combine(_modsPath, "disabled-mod-1.0.disabled")));
        Assert.Empty(_manager.GetInstanceInstalledMods(_instancePath));
    }

    [Fact]
    public async Task InstallModFileToInstanceAsync_InstallsRequiredDependenciesFirst()
    {
        var configStore = new JsonConfigStore(_tempDir);
        configStore.Configuration.CurseForgeKey = "test-key";
        using var httpClient = new HttpClient(new DependencyHandler(
            [
                CreateFileResponse(10, 100, "root.jar", "Root Mod", "https://cdn.test/root.jar",
                    "{\"modId\":20,\"fileId\":200,\"relationType\":4}"),
                CreateFileResponse(20, 200, "dependency.jar", "Dependency Mod", "https://cdn.test/dependency.jar"),
            ]));
        var manager = new ModManager(
            httpClient,
            _tempDir,
            configStore,
            new Mock<IInstanceRepository>().Object,
            new Mock<IProgressReporter>().Object);

        Assert.True(await manager.InstallModFileToInstanceAsync("10", "100", _instancePath));

        var installed = manager.GetInstanceInstalledMods(_instancePath);
        Assert.Equal(["20", "10"], installed.Select(mod => mod.CurseForgeId));
        var root = installed.Single(mod => mod.CurseForgeId == "10");
        var dependency = Assert.Single(root.Dependencies);
        Assert.Equal("20", dependency.ModId);
        Assert.Equal(CurseForgeDependencyRelationType.RequiredDependency, dependency.RelationType);
    }

    [Fact]
    public async Task InstallModFileToInstanceAsync_RejectsCircularDependencies()
    {
        var configStore = new JsonConfigStore(_tempDir);
        configStore.Configuration.CurseForgeKey = "test-key";
        using var httpClient = new HttpClient(new DependencyHandler(
            [
                CreateFileResponse(10, 100, "first.jar", "First Mod", "https://cdn.test/first.jar",
                    "{\"modId\":20,\"fileId\":200,\"relationType\":4}"),
                CreateFileResponse(20, 200, "second.jar", "Second Mod", "https://cdn.test/second.jar",
                    "{\"modId\":10,\"fileId\":100,\"relationType\":4}"),
            ]));
        var manager = new ModManager(
            httpClient,
            _tempDir,
            configStore,
            new Mock<IInstanceRepository>().Object,
            new Mock<IProgressReporter>().Object);

        Assert.False(await manager.InstallModFileToInstanceAsync("10", "100", _instancePath));
        Assert.Empty(manager.GetInstanceInstalledMods(_instancePath));
    }

    [Fact]
    public async Task RemoveInstalledModAsync_ProtectsManifestDependency()
    {
        await WriteArchiveModAsync(
            "dependency.jar",
            "dependency",
            "com.example",
            "dependency",
            "1.0.0");
        await WriteArchiveModAsync(
            "root.jar",
            "root",
            "com.example",
            "root",
            "1.0.0",
            "com.example:dependency");
        await _manager.SaveInstanceModsAsync(_instancePath,
        [
            new InstalledMod
            {
                Id = "dependency",
                Name = "Dependency",
                FileName = "dependency.jar",
            },
            new InstalledMod
            {
                Id = "root",
                Name = "Root",
                FileName = "root.jar",
            }
        ]);

        Assert.False(await _manager.RemoveInstalledModAsync(_instancePath, "dependency"));
        Assert.True(await _manager.RemoveInstalledModAsync(_instancePath, "root"));
        Assert.True(await _manager.RemoveInstalledModAsync(_instancePath, "dependency"));
    }

    private async Task WriteInstalledModAsync(string modId, string fileName)
    {
        await File.WriteAllTextAsync(Path.Combine(_modsPath, fileName), "not a real jar");
        await _manager.SaveInstanceModsAsync(_instancePath,
        [
            new InstalledMod
            {
                Id = modId,
                Name = modId,
                FileName = fileName,
                Enabled = true,
                Version = "1.0",
                Author = "Test Author"
            }
        ]);
    }

    private async Task WriteArchiveModAsync(
        string fileName,
        string id,
        string group,
        string name,
        string version,
        string? dependency = null)
    {
        var archivePath = Path.Combine(_modsPath, fileName);
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        await using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open(), Encoding.UTF8))
        {
            var dependenciesJson = dependency is null
                ? "{}"
                : $"{{\"{dependency}\":\">=1.0.0\"}}";
            await writer.WriteAsync(
                $"{{\"group\":\"{group}\",\"name\":\"{name}\",\"version\":\"{version}\",\"dependencies\":{dependenciesJson}}}");
        }

        await _manager.SaveInstanceModsAsync(_instancePath,
        [new InstalledMod
        {
            Id = id,
            Name = id,
            FileName = fileName,
            Enabled = true,
            Version = version
        }]);
    }

    private static string CreateFileResponse(
        int modId,
        int fileId,
        string fileName,
        string displayName,
        string downloadUrl,
        string? dependency = null)
    {
        var dependencies = dependency is null ? "[]" : $"[{dependency}]";
        return $"{{\"modId\":{modId},\"id\":{fileId},\"fileName\":\"{fileName}\",\"displayName\":\"{displayName}\",\"downloadUrl\":\"{downloadUrl}\",\"dependencies\":{dependencies}}}";
    }

    private sealed class DependencyHandler(IReadOnlyList<string> fileResponses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.StartsWith("/v1/mods/", StringComparison.OrdinalIgnoreCase) &&
                path.Contains("/files/", StringComparison.OrdinalIgnoreCase))
            {
                var response = fileResponses.FirstOrDefault(json =>
                    path.EndsWith($"/{Extract(json, "id")}", StringComparison.Ordinal));
                if (response is not null)
                    return Task.FromResult(JsonResponse($"{{\"data\":{response}}}"));
            }

            if (request.RequestUri?.Host.Equals("cdn.test", StringComparison.OrdinalIgnoreCase) == true)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("test mod archive")
                });

            if (path.StartsWith("/v1/mods/", StringComparison.OrdinalIgnoreCase))
            {
                var modId = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
                var response = fileResponses.FirstOrDefault(json => Extract(json, "modId") == modId);
                var name = response is null ? "Unknown" : ExtractString(response, "displayName");
                return Task.FromResult(JsonResponse($"{{\"data\":{{\"id\":{modId},\"name\":\"{name}\"}}}}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

        private static string Extract(string json, string property)
        {
            var marker = $"\"{property}\":";
            var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = start;
            while (end < json.Length && char.IsDigit(json[end]))
                end++;
            return json[start..end];
        }

        private static string ExtractString(string json, string property)
        {
            var marker = $"\"{property}\":\"";
            var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = json.IndexOf('"', start);
            return json[start..end];
        }
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
                RequestMessage = request
            });
    }
}
