// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Hyprism.Core.Accounts;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Migrations;
using Hyprism.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hyprism.Core.Tests.Migrations;

public sealed class CoreMigrationRunnerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"HyprismMigrationTests_{Guid.NewGuid():N}");

    public CoreMigrationRunnerTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task RunAsync_MigratesLegacyConfigBeforeRepositoriesAndRecordsLocalSteps()
    {
        var uuid = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Combine(_temporaryDirectory, "config.json"), $$"""
            {
              "nick": "LegacyPlayer",
              "uuid": "{{uuid}}",
              "activeProfileIndex": 0
            }
            """);

        var appPath = new AppPathConfiguration(_temporaryDirectory);
        var config = new JsonConfigStore(_temporaryDirectory, deferLegacyMigrations: true);
        var instanceMigrator = new Mock<IInstanceMigrator>();
        var instances = new Mock<IInstanceRepository>();
        instances.Setup(repository => repository.GetCachedInstances()).Returns([]);
        var profiles = new Mock<IProfileRepository>();
        profiles.Setup(repository => repository.GetProfiles()).Returns([]);
        var versions = new Mock<IGameVersionCatalog>();
        var versionNames = new InstanceVersionNameMigrator(instances.Object, versions.Object);

        var services = new ServiceCollection()
            .AddSingleton<IProfileRepository>(profiles.Object)
            .AddSingleton(versionNames)
            .BuildServiceProvider();
        var runner = new CoreMigrationRunner(
            appPath,
            config,
            instanceMigrator.Object,
            instances.Object,
            new MigrationStateStore(appPath),
            services);

        await runner.RunAsync();

        var profilesPath = Path.Combine(_temporaryDirectory, "Profiles", "Profiles.json");
        var migratedProfiles = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(profilesPath));
        var migratedProfile = Assert.Single(migratedProfiles!);
        Assert.Equal("LegacyPlayer", migratedProfile.Name);
        Assert.Equal(uuid, migratedProfile.UUID);
        Assert.Equal(migratedProfile.Id, config.Configuration.SelectedProfileId);
        instanceMigrator.Verify(migration => migration.MigrateLegacyData(), Times.Once);
        instanceMigrator.Verify(migration => migration.MigrateVersionFoldersToIdFolders(), Times.Once);
        instanceMigrator.Verify(migration => migration.MigrateBranchSubdirectoriesToFlat(), Times.Once);
        instances.Verify(repository => repository.SyncInstancesWithConfig(), Times.Once);
        profiles.Verify(repository => repository.GetProfiles(), Times.Once);
        profiles.Verify(repository => repository.MigrateLegacyModsLinks(), Times.Once);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "MigrationState.json")));

        await runner.RunAsync();

        instanceMigrator.Verify(migration => migration.MigrateLegacyData(), Times.Once);
        profiles.Verify(repository => repository.MigrateLegacyModsLinks(), Times.Once);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, true);
    }
}
