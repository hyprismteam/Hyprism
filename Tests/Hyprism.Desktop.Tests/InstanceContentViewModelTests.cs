// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Models;
using Avalonia.Headless.XUnit;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Shell;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class InstanceContentViewModelTests
{
    [AvaloniaFact]
    public async Task ManagerContentUsesItsOwnInstanceWithoutChangingLaunchSelection()
    {
        const string instancePath = "/tmp/hyprism-instance-content-test";
        var managed = new InstanceInfo
        {
            Id = "managed-instance",
            Name = "Managed Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var other = new InstanceInfo
        {
            Id = "other-instance",
            Name = "Other Instance",
            Branch = "pre-release",
            Version = 21
        };
        var selectedForLaunch = new InstanceInfo
        {
            Id = "launch-instance",
            Name = "Launch Instance",
            Branch = "release",
            Version = 19
        };
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
        var modManager = new Mock<IModManager>();

        instances.Setup(service => service.GetCachedInstances()).Returns([managed, other, selectedForLaunch]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(selectedForLaunch);
        instances.Setup(service => service.GetInstancePathById(managed.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        instances.Setup(service => service.GetInstanceMeta(instancePath)).Returns(new InstanceMeta
        {
            Id = managed.Id,
            Name = managed.Name,
            Branch = managed.Branch,
            Version = managed.Version,
            PlayTimeSeconds = 3720
        });
        profiles.Setup(service => service.GetNick()).Returns("Instance Test");
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns(
        [
            new InstalledMod
            {
                Id = "cf-101",
                CurseForgeId = "101",
                Name = "Installed Mod",
                Version = "1.0",
                Author = "Author",
                Enabled = true
            }
        ]);
        modManager.Setup(service => service.SearchModsAsync(
                It.IsAny<string>(), 0, 24, It.IsAny<string[]>(), 2, 1))
            .ReturnsAsync(new ModSearchResult
            {
                Mods =
                [
                    new ModInfo
                    {
                        Id = "202",
                        Name = "Catalog Mod",
                        Author = "Creator",
                        LatestFileId = "303"
                    }
                ],
                TotalCount = 1
            });
        modManager.Setup(service => service.InstallModFileToInstanceAsync(
                "202", "303", instancePath, It.IsAny<Action<string, string>?>()))
            .ReturnsAsync(true);
        launchCoordinator.Setup(service => service.LaunchAsync(
                managed.Id,
                It.IsAny<AuthUriPresenter?>()))
            .Returns(Task.CompletedTask);
        installationWorkflow.Setup(service => service.DownloadAndLaunchInstanceAsync(
                other.Id,
                It.IsAny<AuthUriPresenter?>()))
            .ReturnsAsync(new DownloadProgress { Success = true });
        uriLauncher.Setup(service => service.LaunchDirectoryAsync(instancePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        instances.Setup(service => service.DeleteGameById(other.Id)).Returns(true);

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
            modManager: modManager.Object);

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 1);
        Assert.Equal("Launch Instance", viewModel.SelectedInstanceName);
        Assert.Equal("Managed Instance", viewModel.ManagedInstanceName);
        Assert.Equal(managed.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.Equal("1", viewModel.InstanceModsCountText);
        Assert.Equal("0", viewModel.InstanceWorldsCountText);
        Assert.Equal("1 h 2 min", viewModel.ManagedInstancePlayTime);
        viewModel.CloseInstanceSectionCommand.Execute(null);
        viewModel.SelectInstanceSectionCommand.Execute("logs");
        Assert.True(viewModel.IsInstanceLogsSection);
        Assert.Equal("Console", viewModel.InstanceSectionTitle);
        viewModel.SelectInstanceSectionCommand.Execute("logs");
        Assert.True(viewModel.IsInstanceLogsSection);
        Assert.Equal("Logs", viewModel.InstanceSectionTitle);
        Assert.Equal("Logs", viewModel.DisplayedInstanceSectionTitle);
        viewModel.CloseInstanceSectionCommand.Execute(null);
        Assert.Equal("Logs", viewModel.DisplayedInstanceSectionTitle);
        viewModel.SelectInstanceSectionCommand.Execute("browse");
        await WaitUntilAsync(() => viewModel.ModCatalogItems.Count == 1);
        viewModel.ToggleModCatalogSelectionCommand.Execute(viewModel.ModCatalogItems[0]);
        await viewModel.InstallSelectedCatalogModsCommand.ExecuteAsync(null);

        modManager.Verify(service => service.GetInstanceInstalledMods(instancePath), Times.AtLeastOnce);
        modManager.Verify(service => service.InstallModFileToInstanceAsync(
            "202", "303", instancePath, It.IsAny<Action<string, string>?>()), Times.Once);

        viewModel.OpenInstanceDetailsCommand.Execute(other.Id);
        Assert.Equal("Other Instance", viewModel.ManagedInstanceName);
        Assert.Equal(other.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.Equal("Not Installed", viewModel.ManagedInstanceState);
        Assert.False(viewModel.IsManagedInstanceInstalled);
        Assert.Equal("Launch Instance", viewModel.SelectedInstanceName);
        instances.Verify(service => service.SetSelectedInstance(It.IsAny<string>()), Times.Never);

        viewModel.OpenInstanceDetailsCommand.Execute(managed.Id);
        Assert.Equal("Ready", viewModel.ManagedInstanceState);
        Assert.Equal(managed.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.True(viewModel.IsManagedInstanceInstalled);
        await viewModel.OpenManagedInstanceFolderCommand.ExecuteAsync(null);
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        uriLauncher.Verify(service => service.LaunchDirectoryAsync(
            instancePath,
            It.IsAny<CancellationToken>()), Times.Once);
        launchCoordinator.Verify(service => service.LaunchAsync(
            managed.Id,
            It.IsAny<AuthUriPresenter?>()), Times.Once);

        viewModel.OpenInstanceDetailsCommand.Execute(other.Id);
        Assert.Equal("Not Installed", viewModel.ManagedInstanceState);
        Assert.Equal(other.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.False(viewModel.IsManagedInstanceInstalled);
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        installationWorkflow.Verify(service => service.DownloadAndLaunchInstanceAsync(
            other.Id,
            It.IsAny<AuthUriPresenter?>()), Times.Once);
        instances.Verify(service => service.SetSelectedInstance(It.IsAny<string>()), Times.Never);

        viewModel.MoveInstance(managed.Id, 2);
        Assert.Equal(
            [other.Id, selectedForLaunch.Id, managed.Id],
            viewModel.AllInstances.Select(instance => instance.Id));
        instances.Verify(service => service.SetInstanceOrder(
            It.Is<IReadOnlyList<string>>(ids =>
                ids.SequenceEqual(new[] { other.Id, selectedForLaunch.Id, managed.Id }))), Times.Once);

        viewModel.DeleteManagedInstanceCommand.Execute(null);
        instances.Verify(service => service.DeleteGameById(other.Id), Times.Once);
    }

    [AvaloniaFact]
    public async Task ManagedActionOwnsProgressCancellationAndRunningStateWithoutGlobalNotify()
    {
        var instance = new InstanceInfo
        {
            Id = "action-instance",
            Name = "Action Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
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
        var launchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementLaunchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var launchThreadObserved = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var launchCallCount = 0;

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/tmp/action-instance");
        instances.Setup(service => service.IsClientPresent("/tmp/action-instance")).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Action Test");
        launchCoordinator.Setup(service => service.LaunchAsync(
                instance.Id,
                It.IsAny<AuthUriPresenter?>()))
            .Returns(() =>
            {
                launchThreadObserved.TrySetResult(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
                return Interlocked.Increment(ref launchCallCount) == 1
                    ? launchCompletion.Task
                    : replacementLaunchCompletion.Task;
            });
        gameProcess.Setup(service => service.ExitGame(instance.Id)).Returns(true);
        var gameRunning = false;
        gameProcess.Setup(service => service.IsInstanceRunning(instance.Id)).Returns(() => gameRunning);

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
            new StringLocalizer("en-US"));

        var launchOperation = viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsManagedInstanceActionActive);
        Assert.Equal("Launching", viewModel.ManagedInstanceActionStatusText);
        Assert.Equal("0:00", viewModel.ManagedInstanceActionMetricText);
        Assert.False(await launchThreadObserved.Task);

        gameRunning = true;
        gameProcess.Raise(
            service => service.GameProcessStarted += null!,
            this,
            new GameProcessStartedEventArgs(CreateProcessInfo(instance.Id)));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsManagedInstanceActionRunning);
        Assert.Equal("Running", viewModel.ManagedInstanceActionStatusText);

        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        gameProcess.Verify(service => service.ExitGame(instance.Id), Times.Never);

        viewModel.ArmManagedInstanceCancellation();
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        gameProcess.Verify(service => service.ExitGame(instance.Id), Times.Once);

        gameRunning = false;
        gameProcess.Raise(
            service => service.GameProcessExited += null!,
            this,
            new GameProcessExitedEventArgs(CreateProcessInfo(instance.Id), 0));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsManagedInstanceActionActive);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanRunManagedInstanceAction);

        var replacementLaunch = viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);

        launchCompletion.SetResult();
        await launchOperation;
        Assert.True(viewModel.IsBusy);

        gameProcess.Raise(
            service => service.GameProcessExited += null!,
            this,
            new GameProcessExitedEventArgs(CreateProcessInfo(instance.Id), 0));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        replacementLaunchCompletion.SetResult();
        await replacementLaunch;
    }

    [AvaloniaFact]
    public async Task ManagedActionRestoresRunningInstanceAfterLauncherRestart()
    {
        var instance = new InstanceInfo
        {
            Id = "restored-instance",
            Name = "Restored Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
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

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/tmp/restored-instance");
        instances.Setup(service => service.IsClientPresent("/tmp/restored-instance")).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Restore Test");
        gameProcess.Setup(service => service.IsInstanceRunning(instance.Id)).Returns(true);
        gameProcess.Setup(service => service.ExitGame(instance.Id)).Returns(true);

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
            new StringLocalizer("en-US"));

        Assert.True(viewModel.IsManagedInstanceActionActive);
        Assert.True(viewModel.IsManagedInstanceActionRunning);
        Assert.Equal("Running", viewModel.ManagedInstanceActionStatusText);
        Assert.False(viewModel.CanDeleteManagedInstance);

        viewModel.ArmManagedInstanceCancellation();
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);

        gameProcess.Verify(service => service.ExitGame(instance.Id), Times.Once);
        launchCoordinator.Verify(
            service => service.LaunchAsync(instance.Id, It.IsAny<AuthUriPresenter?>()),
            Times.Never);
    }

    [AvaloniaFact]
    public async Task ManagedInstallShowsProgressAndSecondActionCancelsIt()
    {
        var instance = new InstanceInfo
        {
            Id = "install-instance",
            Name = "Install Instance",
            Branch = "pre-release",
            Version = 21,
            IsInstalled = false
        };
        var otherInstance = new InstanceInfo
        {
            Id = "other-install-instance",
            Name = "Other Install Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = false
        };
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
        var installCompletion = new TaskCompletionSource<DownloadProgress>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        instances.Setup(service => service.GetCachedInstances()).Returns([instance, otherInstance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/tmp/install-instance");
        instances.Setup(service => service.GetInstancePathById(otherInstance.Id))
            .Returns("/tmp/other-install-instance");
        instances.Setup(service => service.IsClientPresent("/tmp/install-instance")).Returns(false);
        instances.Setup(service => service.IsClientPresent("/tmp/other-install-instance")).Returns(false);
        profiles.Setup(service => service.GetNick()).Returns("Install Test");
        installationWorkflow.Setup(service => service.DownloadAndLaunchInstanceAsync(
                instance.Id,
                It.IsAny<AuthUriPresenter?>()))
            .Returns(installCompletion.Task);

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
            new StringLocalizer("en-US"));

        var installOperation = viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        var progressPropertyChanges = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.ActivityProgress))
                progressPropertyChanges++;
        };
        progress.Raise(service => service.DownloadProgressChanged += null!, new ProgressUpdateMessage
        {
            State = "downloading",
            Progress = 12,
            MessageKey = "common.loading"
        });
        progress.Raise(service => service.DownloadProgressChanged += null!, new ProgressUpdateMessage
        {
            State = "downloading",
            Progress = 24,
            MessageKey = "common.loading"
        });
        progress.Raise(service => service.DownloadProgressChanged += null!, new ProgressUpdateMessage
        {
            State = "downloading",
            Progress = 37,
            MessageKey = "common.loading"
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsManagedInstanceActionActive);
        Assert.Equal("Loading...", viewModel.ManagedInstanceActionStatusText);
        Assert.Equal("37%", viewModel.ManagedInstanceActionMetricText);
        Assert.Equal(1, progressPropertyChanges);

        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        installationWorkflow.Verify(service => service.CancelDownload(instance.Id), Times.Never);

        viewModel.ArmManagedInstanceCancellation();
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        installationWorkflow.Verify(service => service.CancelDownload(instance.Id), Times.Once);

        installCompletion.SetResult(new DownloadProgress { Cancelled = true });
        await installOperation;
        Assert.False(viewModel.IsManagedInstanceActionActive);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanRunManagedInstanceAction);

        progress.Raise(service => service.DownloadProgressChanged += null!, new ProgressUpdateMessage
        {
            State = "downloading",
            Progress = 42,
            MessageKey = "common.loading"
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanRunManagedInstanceAction);

        viewModel.OpenInstanceDetailsCommand.Execute(otherInstance.Id);
        Assert.Equal(
            otherInstance.Id,
            Assert.Single(viewModel.AllInstances, item => item.IsManaged).Id);
        Assert.True(viewModel.CanRunManagedInstanceAction);
    }

    private static Task WaitUntilAsync(Func<bool> condition)
        => AvaloniaTestWait.UntilAsync(condition, "instance content state to settle");

    private static GameProcessInfo CreateProcessInfo(string instanceId)
        => new(
            123,
            DateTime.UtcNow,
            instanceId,
            "profile-id",
            null,
            DateTime.UtcNow);
}
