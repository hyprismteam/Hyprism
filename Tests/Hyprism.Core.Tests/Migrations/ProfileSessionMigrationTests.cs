// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Migrations;
using Hyprism.Core.Models;

namespace Hyprism.Core.Tests.Migrations;

public sealed class ProfileSessionMigrationTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"HyprismSessionMigrationTests_{Guid.NewGuid():N}");

    public ProfileSessionMigrationTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void Migrate_MovesRootSessionIntoSelectedProfileOnlyAfterItCanBeRead()
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "LegacyPlayer",
            UUID = Guid.NewGuid().ToString()
        };
        var profilesDirectory = Path.Combine(_temporaryDirectory, "Profiles");
        Directory.CreateDirectory(profilesDirectory);
        File.WriteAllText(
            Path.Combine(profilesDirectory, "Profiles.json"),
            JsonSerializer.Serialize(new[] { profile }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "hytale_session.json"), """
            {
              "access_token": "legacy-access",
              "refresh_token": "legacy-refresh",
              "username": "LegacyPlayer",
              "uuid": "00000000-0000-0000-0000-000000000001"
            }
            """);

        var appPath = new AppPathConfiguration(_temporaryDirectory);
        var config = new JsonConfigStore(_temporaryDirectory);
        config.Configuration.SelectedProfileId = profile.Id;
        config.SaveConfig();

        new ProfileSessionMigration(appPath, config).Migrate();

        var sessionPath = Path.Combine(profilesDirectory, profile.Id, "HytaleSession.json");
        Assert.True(File.Exists(sessionPath));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "HytaleSession.json")));
        var migratedProfiles = JsonSerializer.Deserialize<List<Profile>>(
            File.ReadAllText(Path.Combine(profilesDirectory, "Profiles.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.True(Assert.Single(migratedProfiles!).IsOfficial);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, true);
    }
}
