// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Models;
using Hyprism.Core.Migrations;
using System.Text.Json;

namespace Hyprism.Core.Tests.Game;

public class InstanceRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonConfigStore _config;
    private readonly InstanceRepository _svc;

    public InstanceRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyprismInstanceRepoTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new JsonConfigStore(_tempDir);
        _svc = new InstanceRepository(_tempDir, _config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void CreateInstanceMeta_RaisesInstancesChanged()
    {
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        var meta = _svc.CreateInstanceMeta("release", 42);

        Assert.Equal(InstanceMeta.DefaultName, meta.Name);
        Assert.Equal(1, raised);
        Assert.Single(_svc.GetCachedInstances());
        Assert.Contains(
            Directory.EnumerateFiles(_svc.GetInstanceRoot()),
            path => string.Equals(Path.GetFileName(path), "Instances.json", StringComparison.Ordinal));
        var instancePath = _svc.GetInstancePathById(meta.Id)!;
        var metaPath = Assert.Single(
            Directory.EnumerateFiles(instancePath),
            path => string.Equals(Path.GetFileName(path), "Meta.json", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
        Assert.All(
            document.RootElement.EnumerateObject(),
            property => Assert.True(char.IsUpper(property.Name[0]), property.Name));
    }

    [Fact]
    public void DeleteGameById_RemovesInstanceFromCacheAndRaisesInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var instancePath = _svc.GetInstancePathById(meta.Id)!;
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        Assert.True(_svc.DeleteGameById(meta.Id));

        Assert.Equal(1, raised);
        Assert.DoesNotContain(_svc.GetCachedInstances(), instance => instance.Id == meta.Id);
        Assert.False(Directory.Exists(instancePath));
    }

    [Fact]
    public void DeleteGameById_RemovesInstanceFromLegacyRoot()
    {
        var id = Guid.NewGuid().ToString();
        var instancePath = Path.Combine(_tempDir, "instance", id);
        Directory.CreateDirectory(instancePath);
        _svc.SaveInstanceMeta(instancePath, new InstanceMeta
        {
            Id = id,
            Name = "Legacy Instance",
            Branch = "release",
            Version = 42
        });
        _svc.SyncInstancesWithConfig();

        Assert.Equal(instancePath, _svc.GetInstancePathById(id));
        Assert.True(_svc.DeleteGameById(id));

        Assert.False(Directory.Exists(instancePath));
        Assert.DoesNotContain(_svc.GetCachedInstances(), instance => instance.Id == id);
    }

    [Fact]
    public void DeleteGameById_DropsStaleCacheEntryWhenDirectoryIsMissing()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        Directory.Delete(_svc.GetInstancePathById(meta.Id)!, recursive: true);

        Assert.True(_svc.DeleteGameById(meta.Id));

        Assert.DoesNotContain(_svc.GetCachedInstances(), instance => instance.Id == meta.Id);
    }

    [Fact]
    public void SetInstanceOrder_PersistsOrderAndRaisesInstancesChanged()
    {
        var first = _svc.CreateInstanceMeta("release", 42);
        var second = _svc.CreateInstanceMeta("pre-release", 41);
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        _svc.SetInstanceOrder([second.Id, first.Id]);

        Assert.Equal(1, raised);
        Assert.Equal(
            [second.Id, first.Id],
            _svc.GetCachedInstances().Select(instance => instance.Id));
    }

    [Fact]
    public void SetSelectedInstance_RaisesInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        _svc.SetSelectedInstance(meta.Id);

        Assert.Equal(1, raised);
        Assert.Equal(meta.Id, _svc.GetSelectedInstance()?.Id);
    }

    [Fact]
    public void ChangeInstanceVersion_RaisesInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        Assert.True(_svc.ChangeInstanceVersion(meta.Id, "release", 43));

        Assert.Equal(1, raised);
        Assert.Equal(43, _svc.GetCachedInstances().Single().Version);
    }

    [Fact]
    public void SyncInstancesWithConfig_RaisesInstancesChanged()
    {
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        _svc.SyncInstancesWithConfig();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SyncInstancesWithConfig_ClearsSelectionWhenInstanceWasRemoved()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        _svc.SetSelectedInstance(meta.Id);
        var instancePath = _svc.GetInstancePathById(meta.Id)!;

        Directory.Delete(instancePath, recursive: true);

        _svc.SyncInstancesWithConfig();

        Assert.Empty(_svc.GetCachedInstances());
        Assert.Empty(_config.Configuration.SelectedInstanceId);
        Assert.Null(_svc.GetSelectedInstance());
    }

    [Fact]
    public void SaveInstanceMeta_DoesNotRaiseInstancesChanged()
    {
        var meta = _svc.CreateInstanceMeta("release", 42);
        var path = _svc.GetInstancePathById(meta.Id)!;
        var raised = 0;
        _svc.InstancesChanged += () => raised++;

        var instanceMeta = _svc.GetInstanceMeta(path)!;
        instanceMeta.PlayTimeSeconds += 60;
        _svc.SaveInstanceMeta(path, instanceMeta);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void MigrateLegacyRollingInstancesToFixedVersions_UsesInstalledVersion()
    {
        var instanceId = Guid.NewGuid().ToString();
        var instancePath = Path.Combine(_svc.GetInstanceRoot(), instanceId);
        var legacyMeta = new InstanceMeta
        {
            Id = instanceId,
            Name = "release (Latest)",
            Branch = "release",
            Version = 0,
            InstalledVersion = 42,
            CreatedAt = DateTime.UtcNow
        };
        _svc.SaveInstanceMeta(instancePath, legacyMeta);

        var migrator = new InstanceMigrator(new AppPathConfiguration(_tempDir), _config, _svc);

        Assert.True(migrator.MigrateLegacyRollingInstancesToFixedVersions());

        var migrated = _svc.GetInstanceMeta(instancePath)!;
        Assert.Equal(42, migrated.Version);
        Assert.Equal(42, migrated.InstalledVersion);
        Assert.Equal(0, migrated.PendingVersion);
        Assert.Equal("release v42", migrated.Name);
    }

    [Fact]
    public void MigrateLegacyRollingInstancesToFixedVersions_UsesLegacyMarker()
    {
        var instanceId = Guid.NewGuid().ToString();
        var instancePath = Path.Combine(_svc.GetInstanceRoot(), instanceId);
        var legacyMeta = new InstanceMeta
        {
            Id = instanceId,
            Name = "release (Latest)",
            Branch = "release",
            Version = 0,
            CreatedAt = DateTime.UtcNow
        };
        _svc.SaveInstanceMeta(instancePath, legacyMeta);
        File.WriteAllText(Path.Combine(instancePath, "latest.json"), "{\"Version\":43}");

        var migrator = new InstanceMigrator(new AppPathConfiguration(_tempDir), _config, _svc);

        Assert.True(migrator.MigrateLegacyRollingInstancesToFixedVersions());

        var migrated = _svc.GetInstanceMeta(instancePath)!;
        Assert.Equal(43, migrated.Version);
        Assert.Equal(43, migrated.InstalledVersion);
        Assert.False(File.Exists(Path.Combine(instancePath, "latest.json")));
    }
}
