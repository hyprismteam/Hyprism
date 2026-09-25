// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Models;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class DataSettingsViewModelTests
{
    [AvaloniaFact]
    public async Task BrowseInstanceFolder_MovesDataAndUpdatesTheDisplayedPath()
    {
        var configuredDirectory = "C:\\HyPrism\\Instances";
        var selectedDirectory = "D:\\Games\\HyPrism";
        var settings = CreateSettingsStore(
            () => configuredDirectory,
            "C:\\HyPrism\\Instances",
            "C:\\HyPrism");
        settings
            .Setup(service => service.SetInstanceDirectoryAsync(
                selectedDirectory,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<InstanceDirectoryMoveProgress>?>()))
            .Callback(() => configuredDirectory = selectedDirectory)
            .ReturnsAsync(true);
        var picker = new Mock<IFilePicker>();
        picker
            .Setup(service => service.BrowseFolderAsync(configuredDirectory))
            .ReturnsAsync(selectedDirectory);
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            picker.Object);

        Assert.Equal("Edit", viewModel.ChangeInstanceFolderLabel);
        Assert.Equal("Reset", viewModel.ResetInstanceFolderLabel);
        Assert.True(viewModel.IsDefaultInstanceFolder);
        Assert.False(viewModel.CanResetInstanceFolder);

        await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);

        Assert.Equal(selectedDirectory, viewModel.InstanceFolder);
        Assert.False(viewModel.IsChangingInstanceFolder);
        Assert.False(viewModel.IsDefaultInstanceFolder);
        Assert.True(viewModel.CanResetInstanceFolder);
        settings.Verify(
            service => service.SetInstanceDirectoryAsync(
                selectedDirectory,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<InstanceDirectoryMoveProgress>?>()),
            Times.Once);
    }

    [AvaloniaFact]
    public async Task BrowseInstanceFolder_ExpandsWhileTheFolderPickerIsOpenAndCollapsesWhenClosed()
    {
        const string configuredDirectory = "/home/user/Games/HyPrism";
        var pickerOpened = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pickerResult = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = new Mock<IFilePicker>();
        picker
            .Setup(service => service.BrowseFolderAsync(configuredDirectory))
            .Returns(() =>
            {
                pickerOpened.TrySetResult();
                return pickerResult.Task;
            });
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore(
                () => configuredDirectory,
                configuredDirectory,
                "/home/user/.local/share/HyPrism").Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            picker.Object);
        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Id == "data"));
        var view = new SettingsView
        {
            Width = 1180,
            Height = 760,
            DataContext = viewModel
        };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var browseTask = viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);
        await pickerOpened.Task.WaitAsync(TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsChangingInstanceFolder);
        Assert.False(viewModel.IsMovingInstanceFolder);
        Assert.Empty(viewModel.InstanceFolderChangeMetricText);
        Assert.False(viewModel.IsInstanceFolderChangeCancellationArmed);
        var action = Assert.IsType<Button>(FindDataView(view).FindControl<Button>("SelectInstanceFolderButton"));
        Assert.Contains("active", action.Classes);
        Assert.Contains(
            action.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("managedActionSpinner") && path.IsEffectivelyVisible);
        Assert.Contains(
            action.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Classes.Contains("instanceFolderMoveProgress") && !grid.IsEffectivelyVisible);

        pickerResult.TrySetResult(null);
        await browseTask;
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.IsChangingInstanceFolder);
        Assert.False(viewModel.IsMovingInstanceFolder);
        Assert.DoesNotContain("active", action.Classes);
        window.Close();
    }

    [AvaloniaFact]
    public async Task BrowseInstanceFolder_RequiresPointerExitBeforeItCancelsTheMove()
    {
        const string configuredDirectory = "/home/user/Games/HyPrism";
        const string selectedDirectory = "/mnt/games/HyPrism";
        var moveStarted = new TaskCompletionSource<(
            CancellationToken CancellationToken,
            IProgress<InstanceDirectoryMoveProgress>? Progress)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = CreateSettingsStore(
            () => configuredDirectory,
            configuredDirectory,
            "/home/user/.local/share/HyPrism");
        settings
            .Setup(service => service.SetInstanceDirectoryAsync(
                selectedDirectory,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<InstanceDirectoryMoveProgress>?>()))
            .Returns<string, CancellationToken, IProgress<InstanceDirectoryMoveProgress>?>(
                async (_, cancellationToken, progress) =>
            {
                moveStarted.TrySetResult((cancellationToken, progress));
                var moveCancelled = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(
                    () => moveCancelled.TrySetCanceled(cancellationToken));
                await moveCancelled.Task;
                return true;
            });
        var picker = new Mock<IFilePicker>();
        picker
            .Setup(service => service.BrowseFolderAsync(configuredDirectory))
            .ReturnsAsync(selectedDirectory);
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            picker.Object);

        var moveTask = viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);
        var move = await moveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsChangingInstanceFolder);
        Assert.True(viewModel.IsMovingInstanceFolder);
        Assert.False(viewModel.IsInstanceFolderChangeCancellationArmed);
        Assert.False(move.CancellationToken.IsCancellationRequested);
        move.Progress?.Report(new InstanceDirectoryMoveProgress(42, 100));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("42%", viewModel.InstanceFolderChangeMetricText);

        await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);
        Assert.False(move.CancellationToken.IsCancellationRequested);

        viewModel.ArmInstanceFolderChangeCancellation();
        Assert.True(viewModel.IsInstanceFolderChangeCancellationArmed);

        await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);
        await moveTask;

        Assert.True(move.CancellationToken.IsCancellationRequested);
        Assert.False(viewModel.IsChangingInstanceFolder);
        Assert.False(viewModel.IsMovingInstanceFolder);
        Assert.False(viewModel.IsInstanceFolderChangeCancellationArmed);
        Assert.Equal(configuredDirectory, viewModel.InstanceFolder);
    }

    [AvaloniaFact]
    public async Task ResetInstanceFolder_KeepsTheActionExpandedLongEnoughToCompleteItsTransition()
    {
        var configuredDirectory = "/mnt/games/HyPrism";
        const string defaultDirectory = "/home/user/Games/HyPrism";
        var settings = CreateSettingsStore(
            () => configuredDirectory,
            defaultDirectory,
            "/home/user/.local/share/HyPrism");
        settings
            .Setup(service => service.SetInstanceDirectoryAsync(
                string.Empty,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<InstanceDirectoryMoveProgress>?>()))
            .Callback(() => configuredDirectory = string.Empty)
            .ReturnsAsync(true);
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var resetTask = viewModel.ResetInstanceFolderCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsChangingInstanceFolder);
        Assert.False(resetTask.IsCompleted);
        await resetTask;
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(350));
        Assert.False(viewModel.IsChangingInstanceFolder);
        Assert.Equal(defaultDirectory, viewModel.InstanceFolder);
    }

    [AvaloniaFact]
    public async Task DataFolderActions_OpenBothEffectiveLocations()
    {
        const string instanceDirectory = "/home/user/Games/HyPrism";
        const string launcherDirectory = "/home/user/.local/share/HyPrism";
        var launcher = new Mock<IExternalUriLauncher>();
        launcher
            .Setup(service => service.LaunchDirectoryAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore(
                () => instanceDirectory,
                "/home/user/.local/share/HyPrism/Instances",
                launcherDirectory).Object,
            launcher.Object,
            new StringLocalizer("en-US"));

        await viewModel.OpenInstanceFolderCommand.ExecuteAsync(null);
        await viewModel.OpenLauncherDataFolderCommand.ExecuteAsync(null);

        launcher.Verify(
            service => service.LaunchDirectoryAsync(instanceDirectory, It.IsAny<CancellationToken>()),
            Times.Once);
        launcher.Verify(
            service => service.LaunchDirectoryAsync(launcherDirectory, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [AvaloniaFact]
    public async Task DataPage_UsesStorageChartAndLocksChangesWhileGameIsRunning()
    {
        var running = true;
        var gameProcess = new Mock<IGameProcessTracker>();
        gameProcess.Setup(service => service.IsGameRunning()).Returns(() => running);
        var instances = new Mock<IInstanceRepository>();
        var cachedInstances = new List<InstanceInfo>
        {
            new InstanceInfo { Id = "one" },
            new InstanceInfo { Id = "two" },
            new InstanceInfo { Id = "three" }
        };
        instances.Setup(service => service.GetCachedInstances()).Returns(cachedInstances);
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore(
                () => "/home/user/Games/HyPrism",
                "/home/user/.local/share/HyPrism/Instances",
                "/home/user/.local/share/HyPrism").Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gameProcess: gameProcess.Object,
            instanceRepository: instances.Object);
        var storageLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.StorageUsageItems))
                storageLoaded.TrySetResult();
        };
        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Id == "data"));
        await storageLoaded.Task.WaitAsync(TestContext.Current.CancellationToken);
        var view = new SettingsView
        {
            Width = 1180,
            Height = 760,
            DataContext = viewModel
        };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dataView = FindDataView(view);
        var instanceCard = Assert.IsType<Border>(dataView.FindControl<Border>("InstanceFolderCard"));
        var launcherCard = Assert.IsType<Border>(dataView.FindControl<Border>("LauncherDataCard"));
        var launcherFilesCard = Assert.IsType<Border>(dataView.FindControl<Border>("LauncherFilesCard"));
        var storageLegendCard = Assert.IsType<Border>(dataView.FindControl<Border>("StorageLegendCard"));
        var storageUsageOverview = Assert.IsType<StackPanel>(
            dataView.FindControl<StackPanel>("StorageUsageOverview"));
        var warning = Assert.IsType<Border>(dataView.FindControl<Border>("DataGameRunningWarning"));
        var selectButton = Assert.IsType<Button>(dataView.FindControl<Button>("SelectInstanceFolderButton"));
        var resetButton = Assert.IsType<Button>(dataView.FindControl<Button>("ResetInstanceFolderButton"));
        var openLauncherDataButton = Assert.IsType<Button>(
            dataView.FindControl<Button>("OpenLauncherDataFolderButton"));
        var instancePathSurface = Assert.IsType<Border>(
            dataView.FindControl<Border>("InstanceFolderPathSurface"));
        var launcherPathSurface = Assert.IsType<Border>(
            dataView.FindControl<Border>("LauncherDataPathSurface"));
        Assert.True(instanceCard.IsEffectivelyVisible);
        Assert.True(launcherCard.IsEffectivelyVisible);
        Assert.True(launcherFilesCard.IsEffectivelyVisible);
        Assert.True(storageLegendCard.IsEffectivelyVisible);
        Assert.True(storageUsageOverview.IsEffectivelyVisible);
        Assert.Equal(6, viewModel.StorageUsageItems.Count);
        Assert.Equal("Instances", viewModel.StorageUsageItems[0].Label);
        Assert.Equal("3", viewModel.StorageUsageItems[0].Count);
        Assert.Equal("News", viewModel.StorageUsageItems[3].Label);
        Assert.Equal("151 MB", viewModel.TotalStorageUsage);
        Assert.Equal("Used space 151 MB", viewModel.StorageUsageSummary);
        Assert.True(warning.IsEffectivelyVisible);
        Assert.False(selectButton.IsEnabled);
        Assert.False(resetButton.IsEnabled);
        Assert.True(resetButton.IsEffectivelyVisible);
        Assert.Equal(VerticalAlignment.Center, selectButton.VerticalContentAlignment);
        Assert.Contains("instanceFolderChangeAction", selectButton.Classes);
        Assert.DoesNotContain("active", selectButton.Classes);
        Assert.Contains(
            selectButton.GetVisualDescendants().OfType<TextBlock>(),
            label => label.Text == "Edit" && label.VerticalAlignment == VerticalAlignment.Center);
        Assert.Empty(openLauncherDataButton.GetVisualDescendants().OfType<TextBlock>());
        Assert.Contains(
            openLauncherDataButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            icon => icon.Classes.Contains("dataPathOpenIcon"));
        Assert.Equal(default, instancePathSurface.BorderThickness);
        Assert.Equal(default, launcherPathSurface.BorderThickness);
        Assert.InRange(
            Math.Abs(storageLegendCard.Bounds.Width - launcherFilesCard.Bounds.Width),
            0,
            1);
        var legendItems = storageLegendCard.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("storageLegendItem"))
            .ToArray();
        var legendGrid = Assert.Single(
            storageLegendCard.GetVisualDescendants().OfType<UniformGrid>());
        Assert.Equal(6, legendItems.Length);
        Assert.DoesNotContain(
            storageLegendCard.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("storageLegendCount"));
        Assert.All(legendItems, item => Assert.True(item.Bounds.Width > 250));
        Assert.True(storageLegendCard.ClipToBounds);
        Assert.Equal(new CornerRadius(14), storageLegendCard.CornerRadius);
        Assert.Equal(2, legendGrid.ColumnSpacing);
        Assert.Equal(2, legendGrid.RowSpacing);
        Assert.All(legendItems, item =>
        {
            Assert.Equal(default, item.Margin);
            Assert.Equal(64, item.MinHeight);
        });
        cachedInstances.Add(new InstanceInfo { Id = "four" });
        instances.Raise(service => service.InstancesChanged += null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("4", viewModel.StorageUsageItems[0].Count);
        Assert.Equal(
            "/home/user/Games/HyPrism",
            dataView.FindControl<TextBlock>("InstanceFolderPath")?.Text);
        Assert.Equal(
            "/home/user/.local/share/HyPrism",
            dataView.FindControl<TextBlock>("LauncherDataPath")?.Text);

        running = false;
        gameProcess.Raise(
            service => service.GameProcessExited += null,
            new GameProcessExitedEventArgs(
                new GameProcessInfo(1, DateTime.UtcNow, "instance", "profile", null, DateTime.UtcNow),
                0));
        Dispatcher.UIThread.RunJobs();

        Assert.False(warning.IsEffectivelyVisible);
        Assert.True(selectButton.IsEnabled);
        Assert.True(resetButton.IsEnabled);

        var renderPath = Environment.GetEnvironmentVariable("HYPRISM_DATA_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(renderPath))
            window.CaptureRenderedFrame()!.Save(renderPath, PngBitmapEncoderOptions.Default);

        window.Width = 760;
        Dispatcher.UIThread.RunJobs();
        var dataCategoryButton = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.DataContext is SettingCategoryViewModel { Id: "data" });
        dataCategoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await AvaloniaTestWait.UntilAsync(
            () => storageLegendCard.IsEffectivelyVisible &&
                  Math.Abs(storageLegendCard.Bounds.Width - launcherFilesCard.Bounds.Width) <= 1 &&
                  legendItems.All(item => item.Bounds.Width > 250),
            "compact data settings content to settle");
        Assert.True(storageLegendCard.IsEffectivelyVisible);
        Assert.InRange(
            Math.Abs(storageLegendCard.Bounds.Width - launcherFilesCard.Bounds.Width),
            0,
            1);
        Assert.All(legendItems, item => Assert.True(item.Bounds.Width > 250));

        var compactRenderPath = Environment.GetEnvironmentVariable(
            "HYPRISM_DATA_SETTINGS_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactRenderPath))
            window.CaptureRenderedFrame()!.Save(compactRenderPath, PngBitmapEncoderOptions.Default);

        window.Close();
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore(
        Func<string> instanceDirectory,
        string defaultInstanceDirectory,
        string launcherDataDirectory)
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.GpuPreference).Returns("auto");
        settings.SetupGet(service => service.JavaArguments).Returns(string.Empty);
        settings.SetupGet(service => service.AuthDomain).Returns(string.Empty);
        settings.SetupGet(service => service.CustomJavaPath).Returns(string.Empty);
        settings.SetupGet(service => service.GameEnvironmentVariables).Returns(string.Empty);
        settings.SetupGet(service => service.InstanceDirectory).Returns(instanceDirectory);
        settings.SetupGet(service => service.DefaultInstanceDirectory).Returns(defaultInstanceDirectory);
        settings.SetupGet(service => service.LauncherDataDirectory).Returns(launcherDataDirectory);
        settings
            .Setup(service => service.GetLauncherStorageUsageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LauncherStorageUsage(
                72 * 1024 * 1024,
                18 * 1024 * 1024,
                42 * 1024 * 1024,
                12 * 1024 * 1024,
                3 * 1024 * 1024,
                4 * 1024 * 1024));
        return settings;
    }

    private static SettingsDataView FindDataView(SettingsView view)
        => Assert.Single(view.GetVisualDescendants().OfType<SettingsDataView>());
}
