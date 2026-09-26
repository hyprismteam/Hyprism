// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Models;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Shell;
using Avalonia.Headless.XUnit;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class InstanceWizardViewModelTests
{
    [AvaloniaFact]
    public void SwitchingToCachedBranchDoesNotStartVersionLoading()
    {
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var versionCatalog = new Mock<IGameVersionCatalog>();
        var releaseVersions = new List<int> { 20, 19 };
        var preReleaseVersions = new List<int> { 61, 60 };

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profiles.Setup(service => service.GetNick()).Returns("Wizard Test");
        versionCatalog
            .Setup(service => service.TryGetCachedVersions(
                "release",
                It.IsAny<TimeSpan>(),
                out releaseVersions))
            .Returns(true);
        versionCatalog
            .Setup(service => service.TryGetCachedVersions(
                "pre-release",
                It.IsAny<TimeSpan>(),
                out preReleaseVersions))
            .Returns(true);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            versionCatalog: versionCatalog.Object);

        var changedProperties = new HashSet<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changedProperties.Add(args.PropertyName);
        };

        viewModel.OpenInstanceCreatorCommand.Execute(null);
        viewModel.SetNewInstanceBranchCommand.Execute("pre-release");

        Assert.False(viewModel.IsInstanceVersionsLoading);
        Assert.False(viewModel.IsCreateReleaseBranch);
        Assert.True(viewModel.IsCreatePreReleaseBranch);
        Assert.Contains(nameof(viewModel.IsCreateReleaseBranch), changedProperties);
        Assert.Contains(nameof(viewModel.IsCreatePreReleaseBranch), changedProperties);
        Assert.Equal([61, 60], viewModel.AvailableInstanceVersions.Select(item => item.Version));
        versionCatalog.Verify(
            service => service.GetVersionListAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [AvaloniaFact]
    public void ClosingCreatorKeepsSelectionUntilExitAnimationCompletes()
    {
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var versionCatalog = new Mock<IGameVersionCatalog>();
        var releaseVersions = new List<int> { 20, 19 };
        var preReleaseVersions = new List<int> { 61, 60 };

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profiles.Setup(service => service.GetNick()).Returns("Wizard Test");
        versionCatalog
            .Setup(service => service.TryGetCachedVersions(
                "release",
                It.IsAny<TimeSpan>(),
                out releaseVersions))
            .Returns(true);
        versionCatalog
            .Setup(service => service.TryGetCachedVersions(
                "pre-release",
                It.IsAny<TimeSpan>(),
                out preReleaseVersions))
            .Returns(true);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            versionCatalog: versionCatalog.Object);

        viewModel.OpenInstanceCreatorCommand.Execute(null);
        viewModel.SetNewInstanceBranchCommand.Execute("pre-release");
        Assert.NotNull(viewModel.SelectedNewInstanceVersion);

        viewModel.CloseInstanceCreatorCommand.Execute(null);

        Assert.False(viewModel.IsInstanceCreatorOpen);
        Assert.Equal("pre-release", viewModel.NewInstanceBranch);
        Assert.Equal(61, viewModel.SelectedNewInstanceVersion?.Version);

        viewModel.Instances.CompleteInstanceCreatorClose();

        Assert.Equal("release", viewModel.NewInstanceBranch);
        Assert.Null(viewModel.SelectedNewInstanceVersion);
        Assert.Empty(viewModel.AvailableInstanceVersions);
        Assert.False(viewModel.IsInstanceVersionsLoading);
        Assert.Empty(viewModel.InstanceCreationError);

        viewModel.OpenInstanceCreatorCommand.Execute(null);

        Assert.True(viewModel.IsInstanceCreatorOpen);
        Assert.Equal("release", viewModel.NewInstanceBranch);
        Assert.Equal([20, 19], viewModel.AvailableInstanceVersions.Select(item => item.Version));
    }

    [AvaloniaFact]
    public void BuildNumbersAreShownWithAvailableGameVersions()
    {
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var versionCatalog = new Mock<IGameVersionCatalog>();
        var cachedVersions = new List<CachedVersionEntry>
        {
            new() { Version = 100, VersionName = "build-100" },
            new() { Version = 102, VersionName = "2026.09.08-e1d69dd" },
            new() { Version = 101, VersionName = "0.6.4" }
        };

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profiles.Setup(service => service.GetNick()).Returns("Wizard Test");
        versionCatalog
            .Setup(service => service.TryGetCachedVersionEntries(
                "release",
                It.IsAny<TimeSpan>(),
                out cachedVersions))
            .Returns(true);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            versionCatalog: versionCatalog.Object);

        viewModel.OpenInstanceCreatorCommand.Execute(null);

        Assert.Equal([102, 101, 100],
            viewModel.AvailableInstanceVersions.Select(item => item.Version));
        Assert.Equal(["(2026.09.08-e1d69dd)", "(0.6.4)", null],
            viewModel.AvailableInstanceVersions.Select(item => item.GameVersionLabel));
        Assert.Equal("(2026.09.08-e1d69dd)", viewModel.SelectedNewInstanceVersion?.GameVersionLabel);
    }

    [AvaloniaFact]
    public void CreatingInstanceStoresHytaleAndDisplaysVersionInUi()
    {
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var versionCatalog = new Mock<IGameVersionCatalog>();
        var cachedVersions = new List<CachedVersionEntry>
        {
            new() { Version = 101, VersionName = "0.6.4" }
        };
        var createdMeta = new InstanceMeta
        {
            Id = "created-instance",
            Name = InstanceMeta.DefaultName,
            Branch = "release",
            Version = 101,
            VersionName = "0.6.4"
        };

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        instances.Setup(service => service.CreateInstanceMeta(
                "release",
                101,
                InstanceMeta.DefaultName,
                "0.6.4"))
            .Returns(createdMeta);
        instances.Setup(service => service.FindInstanceById(createdMeta.Id))
            .Returns(new InstanceInfo
            {
                Id = createdMeta.Id,
                Name = InstanceMeta.DefaultName,
                Branch = "release",
                Version = 101,
                VersionName = "0.6.4"
            });
        profiles.Setup(service => service.GetNick()).Returns("Wizard Test");
        versionCatalog
            .Setup(service => service.TryGetCachedVersionEntries(
                "release",
                It.IsAny<TimeSpan>(),
                out cachedVersions))
            .Returns(true);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            versionCatalog: versionCatalog.Object);

        viewModel.OpenInstanceCreatorCommand.Execute(null);
        viewModel.CreateInstanceCommand.Execute(null);

        instances.Verify(service => service.CreateInstanceMeta(
            "release",
            101,
            InstanceMeta.DefaultName,
            "0.6.4"), Times.Once);
        Assert.Equal("Hytale 0.6.4", viewModel.ManagedInstanceName);
    }

    [AvaloniaFact]
    public void DefaultInstanceNameIsExpandedInUiButCustomNamesArePreserved()
    {
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var versionCatalog = new Mock<IGameVersionCatalog>();
        var cachedInstances = new List<InstanceInfo>
        {
            new()
            {
                Id = "default-instance",
                Name = InstanceMeta.DefaultName,
                Branch = "release",
                Version = 101,
                VersionName = "0.6.4"
            },
            new()
            {
                Id = "custom-instance",
                Name = "My World",
                Branch = "release",
                Version = 102,
                VersionName = "0.6.5"
            }
        };

        instances.Setup(service => service.GetCachedInstances()).Returns(cachedInstances);
        instances.Setup(service => service.GetSelectedInstance()).Returns(cachedInstances[0]);
        profiles.Setup(service => service.GetNick()).Returns("Wizard Test");

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            versionCatalog: versionCatalog.Object);

        Assert.Equal(["Hytale 0.6.4", "My World"], viewModel.AllInstances.Select(item => item.Name));
        Assert.Equal("Hytale 0.6.4", viewModel.SelectedInstanceName);
        Assert.Equal("Hytale 0.6.4", viewModel.ManagedInstanceName);
    }

}
