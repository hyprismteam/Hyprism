// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Shell;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class InstanceModsConsoleTests
{
    [AvaloniaFact]
    public async Task ModSelectionAndToggleDriveInstalledModCommands()
    {
        const string instancePath = "/tmp/hyprism-mod-toggle-test";
        var instance = new InstanceInfo
        {
            Id = "mods-instance",
            Name = "Mods Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher) = CreateFakes(instance, instancePath);
        var modManager = new Mock<IModManager>();
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns(
        [
            new InstalledMod
            {
                Id = "cf-1",
                CurseForgeId = "1",
                Name = "First Mod",
                Version = "1.0",
                Author = "Author",
                Enabled = true
            },
            new InstalledMod
            {
                Id = "cf-2",
                CurseForgeId = "2",
                Name = "Second Mod",
                Version = "2.0",
                Author = "Author",
                Enabled = true
            }
        ]);
        modManager.Setup(service => service.SetModEnabledAsync(
                instancePath, "cf-1", false))
            .ReturnsAsync(true);

        using var viewModel = CreateViewModel(
            instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher, modManager);

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 2);

        var first = viewModel.InstalledMods.First(mod => mod.Id == "cf-1");
        first.IsSelected = true;
        Assert.Equal(1, viewModel.SelectedModCount);
        Assert.True(viewModel.HasModSelection);

        await viewModel.ToggleModCommand.ExecuteAsync(first);
        Assert.False(first.IsEnabled);
        modManager.Verify(
            service => service.SetModEnabledAsync(instancePath, "cf-1", false),
            Times.Once);

        viewModel.ClearInstalledModsSelectionCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedModCount);
        Assert.False(viewModel.HasModSelection);
    }

    [AvaloniaFact]
    public async Task DeleteSelectedModsRemovesEachSelectedMod()
    {
        const string instancePath = "/tmp/hyprism-mod-delete-test";
        var instance = new InstanceInfo
        {
            Id = "delete-instance",
            Name = "Delete Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher) = CreateFakes(instance, instancePath);
        var firstMod = new InstalledMod
        {
            Id = "cf-1",
            Name = "First Mod",
            Version = "1.0",
            Author = "Author",
            Enabled = true
        };
        var secondMod = new InstalledMod
        {
            Id = "cf-2",
            Name = "Second Mod",
            Version = "2.0",
            Author = "Author",
            Enabled = true
        };
        var modManager = new Mock<IModManager>();
        modManager.SetupSequence(service => service.GetInstanceInstalledMods(instancePath))
            .Returns([firstMod, secondMod])
            .Returns([]);
        modManager.Setup(service => service.RemoveInstalledModAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        using var viewModel = CreateViewModel(
            instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher, modManager);

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 2);

        foreach (var mod in viewModel.InstalledMods)
            mod.IsSelected = true;
        Assert.Equal(2, viewModel.SelectedModCount);

        await viewModel.DeleteSelectedModsCommand.ExecuteAsync(null);
        modManager.Verify(
            service => service.RemoveInstalledModAsync(instancePath, "cf-1"),
            Times.Once);
        modManager.Verify(
            service => service.RemoveInstalledModAsync(instancePath, "cf-2"),
            Times.Once);
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 0);
        Assert.Equal(0, viewModel.SelectedModCount);
    }

    [AvaloniaFact]
    public async Task LogsSectionStreamsFiltersAndClearsGameLines()
    {
        const string instancePath = "/tmp/hyprism-console-test";
        var instance = new InstanceInfo
        {
            Id = "console-instance",
            Name = "Console Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher) = CreateFakes(instance, instancePath);
        var console = new GameConsoleService();
        console.Append(instance.Id, "OUT", "hello from game");

        using var viewModel = CreateViewModel(
            instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher,
            new Mock<IModManager>(),
            console);

        viewModel.SelectInstanceSectionCommand.Execute("logs");
        Assert.Single(viewModel.LogsLines);
        Assert.Equal("hello from game", viewModel.LogsLines[0].Text);

        console.Append(instance.Id, "ERR", "boom");
        await WaitUntilAsync(() => viewModel.LogsLines.Count == 2);
        Assert.True(viewModel.LogsLines[1].IsError);

        console.Append("other-instance", "OUT", "not ours");
        Assert.Equal(2, viewModel.LogsLines.Count);

        viewModel.LogsSearchQuery = "boom";
        Assert.Single(viewModel.LogsLines);
        Assert.Equal("boom", viewModel.LogsLines[0].Text);

        viewModel.LogsSearchQuery = string.Empty;
        Assert.Equal(2, viewModel.LogsLines.Count);

        viewModel.ClearLogsCommand.Execute(null);
        Assert.Empty(viewModel.LogsLines);

        console.Append(instance.Id, "INFO", "normal output", source: "HytaleClient.AppStartup");
        console.Append(instance.Id, "WARN", "retrying", source: "HytaleClient.Program");
        console.Append(instance.Id, "ERROR", "request failed", source: "HytaleClient.Program");
        console.Append(instance.Id, "TRACE", "at HytaleClient+0x123", source: "HytaleClient.Program",
            isTrace: true);

        viewModel.IsLogsDebugEnabled = false;
        Assert.Equal(new[] { "WARN", "ERROR" }, viewModel.LogsLines.Select(line => line.Level));
        viewModel.IsLogsWarningsEnabled = false;
        Assert.Single(viewModel.LogsLines);
        Assert.Equal("HytaleClient.Program", viewModel.LogsLines[0].Source);
        viewModel.IsLogsTracingEnabled = true;
        Assert.Equal(2, viewModel.LogsLines.Count);
        Assert.True(viewModel.LogsLines[^1].IsTrace);
    }

    [AvaloniaFact]
    public async Task LogsShowInFolderOpensTheCurrentSessionDirectory()
    {
        var instance = new InstanceInfo
        {
            Id = "log-file-instance",
            Name = "Log File Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var directory = Path.Combine(Path.GetTempPath(), "HyprismLogFolderTests_" + Guid.NewGuid());
        try
        {
            var logSession = new LogSessionPaths(directory, DateTimeOffset.Now);
            File.WriteAllText(logSession.GetInstanceLogPath(instance.Id), "log record");
            var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
                gameProcess, progress, settings, news, uriLauncher) =
                CreateFakes(instance, Path.Combine(directory, "instance"));
            uriLauncher.Setup(service => service.LaunchDirectoryAsync(
                    logSession.SessionDirectory, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            using var viewModel = CreateViewModel(
                instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
                gameProcess, progress, settings, news, uriLauncher,
                new Mock<IModManager>(), logSession: logSession);
            viewModel.SelectInstanceSectionCommand.Execute("logs");

            Assert.True(viewModel.CanShowLogsInFolder);
            await viewModel.ShowLogsInFolderCommand.ExecuteAsync(null);
            uriLauncher.Verify(service => service.LaunchDirectoryAsync(
                logSession.SessionDirectory, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task LogMessageCanBeSelectedWithTheMouse()
    {
        var instance = new InstanceInfo
        {
            Id = "selectable-log-instance",
            Name = "Selectable Log Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher) =
            CreateFakes(instance, "/tmp/hyprism-selectable-log-test");
        var console = new GameConsoleService();
        console.Append(instance.Id, "INFO", "select this log message");
        using var viewModel = CreateViewModel(
            instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher,
            new Mock<IModManager>(), console);
        var view = new InstancesView { DataContext = viewModel.Instances };
        var window = new Window { Width = 1180, Height = 760, Content = view };
        window.Show();
        viewModel.SelectInstanceSectionCommand.Execute("logs");
        await WaitUntilAsync(() => view.FindControl<Grid>("InstanceSectionScreen") is
            { IsHitTestVisible: true });
        var message = view.GetVisualDescendants().OfType<SelectableTextBlock>()
            .First(text => text.Classes.Contains("logText") && text.Text == "select this log message");
        var start = message.TranslatePoint(new Point(2, message.Bounds.Height / 2), window);
        var end = message.TranslatePoint(new Point(80, message.Bounds.Height / 2), window);
        Assert.NotNull(start);
        Assert.NotNull(end);
        window.MouseMove(start!.Value);
        window.MouseDown(start.Value, MouseButton.Left);
        window.MouseMove(end!.Value, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end.Value, MouseButton.Left);

        Assert.NotEmpty(message.SelectedText);
        var selectedText = message.SelectedText;
        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        Assert.Equal(selectedText, await window.Clipboard!.TryGetTextAsync());
    }

    private static (
        Mock<IInstanceRepository> Instances,
        Mock<IProfileManager> Profiles,
        Mock<IProfileRepository> ProfileRepository,
        Mock<IGameLaunchCoordinator> LaunchCoordinator,
        Mock<IGameInstallationWorkflow> InstallationWorkflow,
        Mock<IGameProcessTracker> GameProcess,
        Mock<IProgressReporter> Progress,
        Mock<IDesktopSettingsStore> Settings,
        Mock<IHytaleNewsClient> News,
        Mock<IExternalUriLauncher> UriLauncher) CreateFakes(
            InstanceInfo instance,
            string instancePath)
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

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Console Test");

        return (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher);
    }

    private static MainWindowViewModel CreateViewModel(
        Mock<IInstanceRepository> instances,
        Mock<IProfileManager> profiles,
        Mock<IProfileRepository> profileRepository,
        Mock<IGameLaunchCoordinator> launchCoordinator,
        Mock<IGameInstallationWorkflow> installationWorkflow,
        Mock<IGameProcessTracker> gameProcess,
        Mock<IProgressReporter> progress,
        Mock<IDesktopSettingsStore> settings,
        Mock<IHytaleNewsClient> news,
        Mock<IExternalUriLauncher> uriLauncher,
        Mock<IModManager> modManager,
        IGameConsoleService? gameConsole = null,
        LogSessionPaths? logSession = null)
        => new(
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
            modManager: modManager.Object,
            gameConsole: gameConsole,
            logSession: logSession);

    private static Task WaitUntilAsync(Func<bool> condition)
        => AvaloniaTestWait.UntilAsync(condition, "instance view-model state to settle");
}
