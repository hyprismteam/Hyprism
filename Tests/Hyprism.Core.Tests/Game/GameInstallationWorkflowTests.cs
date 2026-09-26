// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core;
using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game.Download;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Patching;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;

namespace Hyprism.Core.Tests.Game;

public sealed class GameInstallationWorkflowTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"HyprismGameInstallationWorkflowTests_{Guid.NewGuid():N}");

    [Fact]
    public async Task DownloadAndLaunchInstanceAsync_InstalledFixedVersionSkipsVersionCatalog()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var instanceId = "release-instance";
        var instancePath = Path.Combine(_temporaryDirectory, "Instances", instanceId);
        var javaPath = Path.Combine(_temporaryDirectory, "java.exe");
        File.WriteAllText(javaPath, string.Empty);

        var instance = new InstanceInfo
        {
            Id = instanceId,
            Name = "Release v42",
            Branch = "release",
            Version = 42,
            IsInstalled = true
        };
        var meta = new InstanceMeta
        {
            Id = instanceId,
            Name = instance.Name,
            Branch = instance.Branch,
            Version = instance.Version,
            InstalledVersion = instance.Version,
            CreatedAt = DateTime.UtcNow
        };

        var config = new Mock<IConfigStore>();
        config.SetupGet(store => store.Configuration).Returns(new Config());

        var instances = new Mock<IInstanceRepository>();
        instances.Setup(repository => repository.FindInstanceById(instanceId)).Returns(instance);
        instances.Setup(repository => repository.GetInstancePathById(instanceId)).Returns(instancePath);
        instances.Setup(repository => repository.IsClientPresent(instancePath)).Returns(true);
        instances.Setup(repository => repository.GetInstanceMeta(instancePath)).Returns(meta);

        var progress = new Mock<IProgressReporter>();
        progress.Setup(reporter => reporter.BeginOperation(instanceId)).Returns(Mock.Of<IDisposable>());

        var runtime = new Mock<IRuntimeProvisioner>();
        runtime.Setup(provisioner => provisioner.GetJavaPath()).Returns(javaPath);
        runtime.Setup(provisioner => provisioner.EnsureVCRedistInstalledAsync(
                It.IsAny<Action<int, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var launcher = new Mock<IGameLauncher>();
        launcher.Setup(game => game.LaunchGameAsync(
                instancePath,
                "release",
                It.IsAny<AuthUriPresenter?>(),
                instanceId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var versions = new Mock<IGameVersionCatalog>();
        var butler = new Mock<IButlerClient>();
        var downloader = new Mock<IFileDownloader>();
        var patches = new Mock<IPatchManager>();

        using var workflow = new GameInstallationWorkflow(
            config.Object,
            instances.Object,
            versions.Object,
            runtime.Object,
            butler.Object,
            downloader.Object,
            progress.Object,
            patches.Object,
            launcher.Object,
            new HttpClient(),
            new AppPathConfiguration(_temporaryDirectory));

        var result = await workflow.DownloadAndLaunchInstanceAsync(instanceId);

        Assert.True(result.Success);
        versions.Verify(
            catalog => catalog.GetVersionListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        launcher.Verify(game => game.LaunchGameAsync(
                instancePath,
                "release",
                It.IsAny<AuthUriPresenter?>(),
                instanceId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, true);
    }
}
