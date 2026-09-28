// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;
using Xunit;

namespace Hyprism.Core.Tests.Game;

public sealed class InstancePackageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hyprism-package-" + Guid.NewGuid());
    private readonly InstanceMeta _meta = new()
    {
        Id = "source-instance",
        Name = "Example",
        Branch = "release",
        Version = 42,
        Notes = "A saved build"
    };

    public InstancePackageServiceTests()
    {
        Write("Meta.json", "{}");
        Write("Client/game.bin", "game");
        Write("UserData/Mods/first.jar", "mod");
        Write("UserData/Mods/Manifest.json", "[]");
        Write("UserData/Saves/world.dat", "world");
    }

    [Theory]
    [InlineData(InstancePackageKind.Build, true, true, true)]
    [InlineData(InstancePackageKind.Modpack, false, true, false)]
    [InlineData(InstancePackageKind.Game, true, false, false)]
    public async Task ZipContainsOnlySelectedContent(
        InstancePackageKind kind, bool game, bool mods, bool world)
    {
        var destination = Path.Combine(_root, kind + ".zip");

        await InstancePackageService.ExportAsync(
            Path.Combine(_root, "source"), _meta, kind,
            InstancePackageFormat.Zip, destination);

        using var archive = ZipFile.OpenRead(destination);
        var names = archive.Entries.Select(entry => entry.FullName).ToHashSet();
        Assert.Contains("Meta.json", names);
        Assert.Contains("HyprismExport.json", names);
        Assert.Equal(game, names.Contains("Client/game.bin"));
        Assert.Equal(mods, names.Contains("UserData/Mods/first.jar"));
        Assert.Equal(mods, names.Contains("UserData/Mods/Manifest.json"));
        Assert.Equal(world, names.Contains("UserData/Saves/world.dat"));
    }

    [Fact]
    public async Task JsonRecordsMetadataWithoutFileContents()
    {
        var destination = Path.Combine(_root, "modpack.json");

        await InstancePackageService.ExportAsync(
            Path.Combine(_root, "source"), _meta, InstancePackageKind.Modpack,
            InstancePackageFormat.Json, destination);
        var package = await InstancePackageService.ReadJsonAsync(destination);

        Assert.Equal("A saved build", package.Instance.Notes);
        Assert.Equal(InstancePackageKind.Modpack, package.Kind);
        Assert.Equal(2, package.Files.Count);
        Assert.All(package.Files, file => Assert.StartsWith("UserData/Mods/", file.Path));
        Assert.False(File.Exists(destination + ".tmp"));
    }

    [Fact]
    public async Task LegacyLowercaseModManifestIsIncludedInMetadata()
    {
        File.Delete(Path.Combine(_root, "source", "UserData", "Mods", "Manifest.json"));
        Write("UserData/Mods/manifest.json", "[{\"Id\":\"sample\",\"Name\":\"Sample\"}]");
        var destination = Path.Combine(_root, "legacy.json");

        await InstancePackageService.ExportAsync(
            Path.Combine(_root, "source"), _meta, InstancePackageKind.Modpack,
            InstancePackageFormat.Json, destination);

        var package = await InstancePackageService.ReadJsonAsync(destination);
        Assert.Equal("Sample", Assert.Single(package.Mods).Name);
        Assert.Contains(package.Files, file => file.Path == "UserData/Mods/manifest.json");
    }

    [Fact]
    public async Task ExportedZipCanBeImportedAsNewInstance()
    {
        var destination = Path.Combine(_root, "build.zip");
        await InstancePackageService.ExportAsync(
            Path.Combine(_root, "source"), _meta, InstancePackageKind.Build,
            InstancePackageFormat.Zip, destination);
        var launcherRoot = Path.Combine(_root, "launcher");
        var repository = new InstanceRepository(
            launcherRoot, new JsonConfigStore(launcherRoot));

        await repository.ImportFromZipAsync(destination);

        var imported = Assert.Single(repository.GetCachedInstances());
        var path = repository.GetInstancePathById(imported.Id)!;
        Assert.True(Guid.TryParse(imported.Id, out _));
        Assert.NotEqual(_meta.Id, imported.Id);
        Assert.Equal("Example", imported.Name);
        Assert.True(File.Exists(Path.Combine(path, "Client", "game.bin")));
        Assert.True(File.Exists(Path.Combine(path, "UserData", "Mods", "first.jar")));
    }

    [Fact]
    public async Task CancelledZipImportRemovesTemporaryExtraction()
    {
        var destination = Path.Combine(_root, "build.zip");
        await InstancePackageService.ExportAsync(
            Path.Combine(_root, "source"), _meta, InstancePackageKind.Build,
            InstancePackageFormat.Zip, destination);
        var launcherRoot = Path.Combine(_root, "launcher");
        var repository = new InstanceRepository(
            launcherRoot, new JsonConfigStore(launcherRoot));
        var before = Directory.EnumerateDirectories(Path.GetTempPath(), "hyprism-import-*")
            .ToHashSet(StringComparer.Ordinal);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.ImportFromZipAsync(destination, cancellation.Token));

        Assert.Empty(Directory.EnumerateDirectories(Path.GetTempPath(), "hyprism-import-*")
            .Except(before, StringComparer.Ordinal));
        Assert.Empty(repository.GetCachedInstances());
    }

    [Fact]
    public async Task CancelledExportDoesNotReplaceDestination()
    {
        var destination = Path.Combine(_root, "existing.zip");
        File.WriteAllText(destination, "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InstancePackageService.ExportAsync(
                Path.Combine(_root, "source"), _meta, InstancePackageKind.Build,
                InstancePackageFormat.Zip, destination, cancellation.Token));

        Assert.Equal("original", File.ReadAllText(destination));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp-*"));
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, "source", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
