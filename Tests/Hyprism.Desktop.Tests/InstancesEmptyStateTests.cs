// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Game.Versions;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Shell;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class InstancesEmptyStateTests
{
    [AvaloniaFact]
    public void EmptyStateTracksDownloadSourceAvailability()
    {
        using var httpClient = new HttpClient();
        var hasSources = false;
        var versions = new Mock<IGameVersionCatalog>();
        versions.Setup(catalog => catalog.HasDownloadSources()).Returns(() => hasSources);
        using var viewModel = MainWindowViewModelFactory.Create(
            httpClient, versionCatalog: versions.Object, emptyInstances: true);
        var window = new MainWindow
        {
            Width = 1280,
            Height = 800,
            DataContext = viewModel
        };
        window.Show();
        viewModel.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();

        var instances = window.GetVisualDescendants().OfType<InstancesView>().Single();
        var button = instances.FindControl<Button>("EmptyCreateButton")!;
        var note = instances.FindControl<NoteCard>("EmptySourcesNote")!;

        Assert.False(button.IsEnabled);
        Assert.True(note.IsEffectivelyVisible);
        Assert.False(viewModel.Instances.HasDownloadSources);
        Assert.Equal(560, note.Bounds.Width);
        Assert.Contains("wizardAction", button.Classes);
        Assert.Contains("create", button.Classes);
        Assert.Equal(0.45, button.Opacity);
        Assert.Contains(button.Transitions!, transition => transition is BrushTransition);

        hasSources = true;
        viewModel.Instances.RefreshDownloadSourceAvailability();
        Dispatcher.UIThread.RunJobs();

        Assert.True(button.IsEnabled);
        Assert.False(note.IsEffectivelyVisible);
        window.Close();
    }
}
