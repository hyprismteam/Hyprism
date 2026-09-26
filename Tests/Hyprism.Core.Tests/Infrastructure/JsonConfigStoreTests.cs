// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Tests.Core.Infrastructure;

/// <summary>
/// Tests loading, saving, resetting, and migration behavior in <see cref="JsonConfigStore"/>
/// All tests use temporary directories to isolate I/O
/// </summary>
public class JsonConfigStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyprismTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }


    [Fact]
    public void Constructor_NoConfigFile_CreatesDefaultConfig()
    {
        var svc = new JsonConfigStore(_tempDir);

        Assert.NotNull(svc.Configuration);
        Assert.Empty(svc.Configuration.SelectedProfileId);
        AssertExactFileName(_tempDir, "Config.json");
        AssertConfigContainsNoLegacyProfileFields();
    }

    [Fact]
    public void SaveConfig_PersistsSelectedProfileId()
    {
        var svc = new JsonConfigStore(_tempDir);
        svc.Configuration.SelectedProfileId = "profile-id";
        svc.SaveConfig();

        var json = File.ReadAllText(Path.Combine(_tempDir, "Config.json"));
        Assert.Contains("profile-id", json);
        using var document = JsonDocument.Parse(json);
        Assert.All(
            document.RootElement.EnumerateObject(),
            property => Assert.True(char.IsUpper(property.Name[0]), property.Name));
        AssertConfigContainsNoLegacyProfileFields();
    }

    [Fact]
    public void Constructor_ExistingConfig_LoadsPersistedValues()
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "SavedPlayer",
            UUID = Guid.NewGuid().ToString()
        };
        var profilesDirectory = Path.Combine(_tempDir, "Profiles");
        Directory.CreateDirectory(profilesDirectory);
        File.WriteAllText(
            Path.Combine(profilesDirectory, "profiles.json"),
            JsonSerializer.Serialize(new[] { profile }));

        var cfg = new Config { SelectedProfileId = profile.Id, Language = "en-US" };
        File.WriteAllText(
            Path.Combine(_tempDir, "config.json"),
            JsonSerializer.Serialize(cfg));

        var svc = new JsonConfigStore(_tempDir);
        Assert.Equal(profile.Id, svc.Configuration.SelectedProfileId);
        Assert.Equal("en-US", svc.Configuration.Language);
        AssertExactFileName(_tempDir, "Config.json");
        AssertExactFileName(profilesDirectory, "Profiles.json");
    }


    [Fact]
    public void ResetConfig_ReplacesConfigWithDefaults()
    {
        var svc = new JsonConfigStore(_tempDir);
        svc.Configuration.SelectedProfileId = "custom-profile";
        svc.Configuration.MusicEnabled = false;

        svc.ResetConfig();

        // Default MusicEnabled is true
        Assert.True(svc.Configuration.MusicEnabled);
    }


    [Fact]
    public async Task SetInstanceDirectoryAsync_ValidPath_SetsAndPersists()
    {
        var svc = new JsonConfigStore(_tempDir);
        var target = Path.Combine(_tempDir, "custom_instances");

        var result = await svc.SetInstanceDirectoryAsync(target);

        Assert.Equal(target, result);
        Assert.Equal(target, svc.Configuration.InstanceDirectory);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public async Task SetInstanceDirectoryAsync_EmptyPath_ClearsDirectory()
    {
        var svc = new JsonConfigStore(_tempDir);
        await svc.SetInstanceDirectoryAsync(Path.Combine(_tempDir, "some_dir"));

        var result = await svc.SetInstanceDirectoryAsync("");

        Assert.Null(result);
        Assert.True(string.IsNullOrEmpty(svc.Configuration.InstanceDirectory));
    }


    [Fact]
    public void Constructor_LegacyIdentity_MovesProfileToProfilesFileAndCleansConfig()
    {
        var uuid = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), $$"""
            {
              "Nick": "СтарыйИгрок",
              "UUID": "{{uuid}}",
              "ActiveProfileIndex": 0,
              "Language": "en-US"
            }
            """);

        var svc = new JsonConfigStore(_tempDir);

        var profilesPath = Path.Combine(_tempDir, "Profiles", "Profiles.json");
        var profiles = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(profilesPath));

        var profile = Assert.Single(profiles!);
        Assert.Equal("СтарыйИгрок", profile.Name);
        Assert.Equal(uuid, profile.UUID);
        Assert.Equal(profile.Id, svc.Configuration.SelectedProfileId);
        Assert.Contains("СтарыйИгрок", File.ReadAllText(profilesPath));
        AssertConfigContainsNoLegacyProfileFields();
    }

    [Fact]
    public void Constructor_EmbeddedProfiles_MovesProfilesAndSelectedIndexOutOfConfig()
    {
        var first = new Profile { Id = Guid.NewGuid().ToString(), Name = "First", UUID = Guid.NewGuid().ToString() };
        var second = new Profile { Id = Guid.NewGuid().ToString(), Name = "Second", UUID = Guid.NewGuid().ToString() };
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), JsonSerializer.Serialize(new
        {
            Profiles = new[] { first, second },
            ActiveProfileIndex = 1,
            Language = "xx-XX"
        }));

        var svc = new JsonConfigStore(_tempDir);

        var profilesPath = Path.Combine(_tempDir, "Profiles", "Profiles.json");
        var profiles = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(profilesPath));
        Assert.Equal(2, profiles!.Count);
        Assert.Equal(second.Id, svc.Configuration.SelectedProfileId);
        Assert.Equal("xx-XX", svc.Configuration.Language);
        AssertConfigContainsNoLegacyProfileFields();
    }

    [Fact]
    public void Constructor_RemovedHostSettings_PreservesThemWhenCanonicalizingConfig()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), """
            {
              "launcherBranch": "beta",
              "installedLauncherBranch": "release",
              "launchAfterDownload": false,
              "accentColor": "#112233",
              "backgroundMode": "custom.png",
              "useDualAuth": false
            }
            """);

        _ = new JsonConfigStore(_tempDir);

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, "Config.json")));
        var properties = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase);

        Assert.Equal("beta", properties["launcherBranch"].GetString());
        Assert.False(properties["launchAfterDownload"].GetBoolean());
        Assert.Equal("#112233", properties["accentColor"].GetString());
        Assert.False(properties["useDualAuth"].GetBoolean());
    }


    [Fact]
    public void Constructor_CorruptJson_CreatesDefaultConfig()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{ this is not json }}}");

        var svc = new JsonConfigStore(_tempDir);

        Assert.NotNull(svc.Configuration);
        Assert.Empty(svc.Configuration.SelectedProfileId);
        AssertConfigContainsNoLegacyProfileFields();
    }

    private void AssertConfigContainsNoLegacyProfileFields()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_tempDir, "Config.json")));
        var propertyNames = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Nick", propertyNames);
        Assert.DoesNotContain("UUID", propertyNames);
        Assert.DoesNotContain("Profiles", propertyNames);
        Assert.DoesNotContain("ActiveProfileIndex", propertyNames);
    }

    private static void AssertExactFileName(string directory, string expectedFileName)
    {
        Assert.Contains(
            Directory.EnumerateFiles(directory),
            path => string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.Ordinal));
    }
}
