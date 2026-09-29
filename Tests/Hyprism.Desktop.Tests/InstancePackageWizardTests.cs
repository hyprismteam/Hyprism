// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Versions;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Shell;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class InstancePackageWizardTests
{
    [AvaloniaFact]
    public async Task ExportWizardUsesSharedRevealAndDefaultsToJson()
    {
        using var httpClient = new HttpClient();
        using var launcher = MainWindowViewModelFactory.Create(httpClient);
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = launcher };
        window.Show();
        launcher.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();

        launcher.Instances.OpenInstanceExportCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(launcher.Instances.IsInstanceCreatorOpen);
        Assert.Equal(InstanceWizardStage.ExportKind, launcher.Instances.InstanceWizardStage);
        Assert.Equal(0, launcher.Instances.ExportFormatIndex);
        var instancesView = Assert.Single(window.GetVisualDescendants().OfType<InstancesView>());
        var creator = instancesView.FindControl<InstanceCreatorView>("InstanceCreatorContentView")!;
        var reveal = creator.FindControl<WizardRevealIcon>("InstanceWizardReveal")!;
        Assert.True(creator.StepControl(InstanceWizardStage.ExportKind).IsVisualAncestorOf(
            creator.FindControl<TextBlock>("InstanceExportTitle")!));
        Assert.True(creator.StepControl(InstanceWizardStage.ExportFormat).IsVisualAncestorOf(
            creator.FindControl<TextBlock>("InstanceExportFormatTitle")!));
        Assert.Equal("/Assets/Lotties/share-reveal.json", reveal.Animation.Path);
        var wizard = instancesView.FindControl<Border>("InstanceCreatorScreen")!;
        await AvaloniaTestWait.UntilAsync(
            () => wizard.IsHitTestVisible && reveal.LastSelectionWasAnimated,
            "instance export wizard to finish opening");
        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/Assets/Lotties/share-reveal.light.json", reveal.Animation.Path);

        await AvaloniaTestWait.UntilAsync(
            () => creator.StepControl(InstanceWizardStage.ExportKind).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.ExportKind).Opacity >= 0.99,
            "instance export choices to open");
        reveal.ShowFinalFrame("/Assets/Lotties/share-reveal.json");
        creator.FindControl<Button>("InstanceExportBuildButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await AvaloniaTestWait.UntilAsync(
            () => launcher.Instances.InstanceWizardStage == InstanceWizardStage.ExportFormat &&
                  creator.StepControl(InstanceWizardStage.ExportFormat).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.ExportFormat).Opacity >= 0.99,
            "instance export format step to open");
        Assert.False(reveal.LastSelectionWasAnimated);
        Assert.Equal(InstanceWizardStage.ExportFormat, launcher.Instances.InstanceWizardStage);
        launcher.Instances.ExportFormatIndex = 1;
        launcher.Instances.BackInstanceWizardCommand.Execute(null);
        Assert.Equal(InstanceWizardStage.ExportKind, launcher.Instances.InstanceWizardStage);
        launcher.Instances.BackInstanceWizardCommand.Execute(null);
        Assert.False(launcher.Instances.IsInstanceCreatorOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EmptyScreenAllowsImportWithoutDownloadSources()
    {
        var versions = new Mock<IGameVersionCatalog>();
        versions.Setup(catalog => catalog.HasDownloadSources()).Returns(false);
        using var httpClient = new HttpClient();
        using var launcher = MainWindowViewModelFactory.Create(
            httpClient, versionCatalog: versions.Object, emptyInstances: true);
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = launcher };
        window.Show();
        launcher.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();

        var instances = Assert.Single(window.GetVisualDescendants().OfType<InstancesView>());
        var import = instances.FindControl<Button>("EmptyImportButton")!;
        var create = instances.FindControl<Button>("EmptyCreateButton")!;
        Assert.True(import.IsEffectivelyVisible);
        Assert.False(create.IsEnabled);
        Assert.True(import.IsEnabled);
        Assert.True(import.Bounds.Right < create.Bounds.Left);
        Assert.InRange(Math.Abs(import.Bounds.Center.Y - create.Bounds.Center.Y), 0, 1);

        import.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(InstanceWizardStage.Import, launcher.Instances.InstanceWizardStage);
        Assert.False(launcher.Instances.CanReturnToInstanceChoice);
        var creator = instances.FindControl<InstanceCreatorView>("InstanceCreatorContentView")!;
        await AvaloniaTestWait.UntilAsync(
            () => launcher.Instances.InstanceWizardStage == InstanceWizardStage.Import &&
                  creator.StepControl(InstanceWizardStage.Import).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.Import).Opacity >= 0.99,
            "instance import step to open");
        Assert.False(creator.StepControl(InstanceWizardStage.Choice).IsVisible);
        var importAction = creator.FindControl<Button>("InstancePackageImportButton")!;
        var backAction = creator.FindControl<Button>("InstanceImportBackButton")!;
        Assert.True(creator.StepControl(InstanceWizardStage.Import).IsVisualAncestorOf(
            creator.FindControl<TextBlock>("InstanceImportTitle")!));
        Assert.True(creator.StepControl(InstanceWizardStage.Import).IsVisualAncestorOf(importAction));
        Assert.True(creator.StepControl(InstanceWizardStage.Import).IsVisualAncestorOf(backAction));
        await AvaloniaTestWait.UntilAsync(
            () => backAction.Bounds.Right < importAction.Bounds.Left,
            "instance import actions to finish layout");
        launcher.Instances.IsImportingInstancePackage = true;
        await AvaloniaTestWait.UntilAsync(
            () => importAction.Bounds.Width >= 271,
            "instance import action to expand");
        Assert.Contains("active", importAction.Classes);
        launcher.Instances.IsImportingInstancePackage = false;
        backAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(launcher.Instances.IsInstanceCreatorOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EmptyScreenWithDownloadSourcesOpensEachDestinationDirectly()
    {
        var versions = new Mock<IGameVersionCatalog>();
        versions.Setup(catalog => catalog.HasDownloadSources()).Returns(true);
        using var httpClient = new HttpClient();
        using var launcher = MainWindowViewModelFactory.Create(
            httpClient, versionCatalog: versions.Object, emptyInstances: true);
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = launcher };
        window.Show();
        launcher.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();

        var instances = Assert.Single(window.GetVisualDescendants().OfType<InstancesView>());
        var import = instances.FindControl<Button>("EmptyImportButton")!;
        var create = instances.FindControl<Button>("EmptyCreateButton")!;
        Assert.True(import.IsEffectivelyVisible);
        Assert.True(create.IsEnabled);

        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(InstanceWizardStage.Download, launcher.Instances.InstanceWizardStage);
        Assert.False(launcher.Instances.CanReturnToInstanceChoice);
        var creator = instances.FindControl<InstanceCreatorView>("InstanceCreatorContentView")!;
        await AvaloniaTestWait.UntilAsync(
            () => creator.StepControl(InstanceWizardStage.Download).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.Download).Opacity >= 0.99,
            "direct instance download step to open");
        Assert.False(creator.StepControl(InstanceWizardStage.Choice).IsVisible);
        Assert.True(instances.TryNavigateBack());
        Assert.False(launcher.Instances.IsInstanceCreatorOpen);
        var wizard = instances.FindControl<Border>("InstanceCreatorScreen")!;
        await AvaloniaTestWait.UntilAsync(() => !wizard.IsVisible, "direct download step to close");

        import.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(InstanceWizardStage.Import, launcher.Instances.InstanceWizardStage);
        Assert.False(launcher.Instances.CanReturnToInstanceChoice);
        await AvaloniaTestWait.UntilAsync(
            () => creator.StepControl(InstanceWizardStage.Import).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.Import).Opacity >= 0.99,
            "direct instance import step to open");
        Assert.False(creator.StepControl(InstanceWizardStage.Choice).IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ExistingInstancesKeepTheCreationChoice()
    {
        using var httpClient = new HttpClient();
        using var launcher = MainWindowViewModelFactory.Create(httpClient);
        Assert.True(launcher.Instances.HasInstances);

        launcher.Instances.OpenInstanceCreatorCommand.Execute(null);

        Assert.Equal(InstanceWizardStage.Choice, launcher.Instances.InstanceWizardStage);
        Assert.True(launcher.Instances.CanReturnToInstanceChoice);
    }
}
