// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Runtime.InteropServices;
using Hyprism.Core.Game.Patching;
using Moq;
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
    public async Task ZipReportsByteProgressAndCompletesAfterReplacingDestination()
    {
        var destination = Path.Combine(_root, "progress.zip");
        var percentages = new List<int>();
        var destinationExistsAtCompletion = false;
        var progress = new InlineProgress(percent =>
        {
            percentages.Add(percent);
            if (percent == 100)
                destinationExistsAtCompletion = File.Exists(destination);
        });

        await InstancePackageService.ExportAsync(
            Path.Combine(_root, "source"), _meta, InstancePackageKind.Build,
            InstancePackageFormat.Zip, destination, progress: progress);

        Assert.Equal(0, percentages[0]);
        Assert.Contains(percentages, percent => percent is > 0 and < 100);
        Assert.Equal(100, percentages[^1]);
        Assert.True(percentages.SequenceEqual(percentages.Order()));
        Assert.True(destinationExistsAtCompletion);
    }

    [Fact]
    public async Task JsonRecordsMetadataWithoutFileContents()
    {
        var destination = Path.Combine(_root, "template.json");

        await InstancePackageService.ExportAsync(
            Path.Combine(_root, "source"), _meta, InstancePackageKind.Build,
            InstancePackageFormat.Json, destination);
        var package = await InstancePackageService.ReadJsonAsync(destination);
        var json = await File.ReadAllTextAsync(destination);

        Assert.Equal(2, package.SchemaVersion);
        Assert.Equal("A saved build", package.Instance.Notes);
        Assert.Equal("Example", package.Instance.Name);
        Assert.DoesNotContain("\"Files\"", json);
        Assert.DoesNotContain("\"Mods\"", json);
        Assert.DoesNotContain("\"Id\"", json);
        Assert.False(File.Exists(destination + ".tmp"));
    }

    [Fact]
    public async Task LegacySchemaOneJsonRemainsImportable()
    {
        var destination = Path.Combine(_root, "legacy.json");
        await File.WriteAllTextAsync(destination,
            """
            {"SchemaVersion":1,"Kind":"Modpack","Instance":{"Id":"old-id","Name":"Example","Branch":"release","Version":42,"Notes":"A saved build"},"Files":[{"Path":"UserData/Mods/first.jar","Size":3}],"Mods":[{"Name":"Sample"}]}
            """);

        var package = await InstancePackageService.ReadJsonAsync(destination);
        Assert.Equal(1, package.SchemaVersion);
        Assert.Equal("Example", package.Instance.Name);
        Assert.Equal(42, package.Instance.Version);
        Assert.Equal("A saved build", package.Instance.Notes);
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

    [Theory]
    [InlineData("0_to_42.pwr")]
    [InlineData("v42-linux-amd64.pwr")]
    [InlineData("42.PWR")]
    public async Task FullPwrCreatesInstalledInstanceWithoutTouchingExistingFiles(string filename)
    {
        var pwr = Path.Combine(_root, filename);
        await File.WriteAllTextAsync(pwr, "original PWR");
        var butler = new Mock<IButlerClient>();
        var launcherRoot = Path.Combine(_root, "launcher");
        var repository = new InstanceRepository(launcherRoot, new JsonConfigStore(launcherRoot), butler.Object);
        var existing = repository.CreateInstanceMeta("release", 42, "Existing");
        var existingPath = repository.GetInstancePathById(existing.Id)!;
        await File.WriteAllTextAsync(Path.Combine(existingPath, "keep.txt"), "existing files");
        butler.Setup(client => client.ApplyPwrAsync(pwr, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Returns((string _, string target, Action<int, string>? _, CancellationToken _) =>
            {
                WritePwrClient(target);
                Directory.CreateDirectory(Path.Combine(target, "UserData", "Mods"));
                File.WriteAllText(Path.Combine(target, "UserData", "Mods", "example.jar"), "mod");
                Assert.Single(repository.GetCachedInstances());
                return Task.CompletedTask;
            });

        await repository.ImportFromPwrAsync(pwr);

        var imported = Assert.Single(repository.GetCachedInstances(), instance => instance.Id != existing.Id);
        var path = repository.GetInstancePathById(imported.Id)!;
        var meta = repository.GetInstanceMeta(path)!;
        Assert.Equal(42, meta.Version);
        Assert.Equal(42, meta.InstalledVersion);
        Assert.Equal("release", meta.Branch);
        Assert.True(repository.IsClientPresent(path));
        Assert.Equal(InstanceValidationStatus.Valid, repository.ValidateGameIntegrity(path).Status);
        Assert.Equal("mod", File.ReadAllText(Path.Combine(path, "UserData", "Mods", "example.jar")));
        Assert.Equal("existing files", File.ReadAllText(Path.Combine(existingPath, "keep.txt")));
        Assert.Equal("original PWR", File.ReadAllText(pwr));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(repository.GetInstanceRoot(), ".import")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledPwrImportDoesNotRegisterPartialInstance(bool cancel)
    {
        var pwr = Path.Combine(_root, "0_to_42.pwr");
        await File.WriteAllTextAsync(pwr, "PWR");
        var butler = new Mock<IButlerClient>();
        var launcherRoot = Path.Combine(_root, "launcher");
        var repository = new InstanceRepository(launcherRoot, new JsonConfigStore(launcherRoot), butler.Object);
        using var cancellation = new CancellationTokenSource();
        butler.Setup(client => client.ApplyPwrAsync(pwr, It.IsAny<string>(), null, cancellation.Token))
            .Returns((string _, string target, Action<int, string>? _, CancellationToken token) =>
            {
                WritePwrClient(target);
                if (cancel)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                throw new InvalidDataException("Invalid PWR");
            });

        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ImportFromPwrAsync(pwr, cancellation.Token));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.ImportFromPwrAsync(pwr, cancellation.Token));

        Assert.Empty(repository.GetCachedInstances());
        Assert.Empty(repository.GetInstalledInstances());
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(repository.GetInstanceRoot(), ".import")));
    }

    [Theory]
    [InlineData("unknown.pwr")]
    [InlineData("0.pwr")]
    [InlineData("41_to_42.pwr")]
    [InlineData("v41~42-linux-amd64.pwr")]
    public async Task PwrWithoutFullBuildFilenameIsRejectedBeforeApplying(string filename)
    {
        var pwr = Path.Combine(_root, filename);
        await File.WriteAllTextAsync(pwr, "PWR");
        var butler = new Mock<IButlerClient>(MockBehavior.Strict);
        var repository = new InstanceRepository(_root, new JsonConfigStore(_root), butler.Object);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ImportFromPwrAsync(pwr));

        butler.VerifyNoOtherCalls();
        Assert.Empty(repository.GetCachedInstances());
    }

    [Fact]
    public async Task PwrWithoutPlatformClientIsNotRegistered()
    {
        var pwr = Path.Combine(_root, "42.pwr");
        await File.WriteAllTextAsync(pwr, "PWR");
        var butler = new Mock<IButlerClient>();
        butler.Setup(client => client.ApplyPwrAsync(pwr, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var repository = new InstanceRepository(_root, new JsonConfigStore(_root), butler.Object);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ImportFromPwrAsync(pwr));

        Assert.Empty(repository.GetCachedInstances());
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(repository.GetInstanceRoot(), ".import")));
    }

    private static void WritePwrClient(string target)
    {
        var relative = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Client/HytaleClient.exe"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Client/Hytale.app/Contents/MacOS/HytaleClient"
            : "Client/HytaleClient";
        var client = Path.Combine(target, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(client)!);
        File.WriteAllText(client, "client");
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, "source", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
