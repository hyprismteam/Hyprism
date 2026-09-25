// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Accounts;
using Hyprism.Core.Models;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Profiles;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class WizardAnimationTests
{
    [AvaloniaFact]
    public async Task NavigationPaneTransitionKeepsLayoutStableUntilOverviewIsHidden()
    {
        var overview = new Border { RenderTransform = new TranslateTransform() };
        var wizard = new Border { RenderTransform = new TranslateTransform() };
        var pane = new Border
        {
            Width = 276,
            RenderTransform = new TranslateTransform(),
            Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(190) },
                new DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromMilliseconds(190) }
            }
        };
        Assert.IsType<TranslateTransform>(pane.RenderTransform).Transitions = new Transitions
        {
            new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(190) }
        };
        var contentHost = new Grid
        {
            MaxWidth = 600,
            Children = { overview, wizard }
        };
        Grid.SetColumn(contentHost, 1);
        var window = new Window
        {
            Width = 800,
            Height = 700,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children = { pane, contentHost }
            }
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        var overviewWidth = contentHost.Bounds.Width;

        var transition = new WizardScreenTransition(overview, wizard, pane);
        transition.HideNavigationPane(animate: true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(pane.IsAnimating(Layoutable.WidthProperty));
        Assert.True(pane.IsAnimating(Visual.OpacityProperty));
        Assert.Equal(276, pane.Bounds.Width);
        Assert.Equal(overviewWidth, contentHost.Bounds.Width);

        await transition.OpenAsync(() => true);

        Assert.Equal(0, pane.Bounds.Width);
        Assert.Equal(0, pane.Opacity);
        Assert.True(contentHost.Bounds.Width > overviewWidth);

        window.Close();
    }

    [AvaloniaFact]
    public async Task CompactProfileCreatorOpeningKeepsItsDetailSlide()
    {
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(
        [
            new Profile { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() }
        ]);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active");

        using var profilesViewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));
        var view = new ProfilesView { DataContext = profilesViewModel };
        var window = new Window
        {
            Width = 760,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var profileMain = Assert.IsType<Grid>(view.FindControl<Grid>("ProfileMain"));
        var translation = Assert.IsType<TranslateTransform>(profileMain.RenderTransform);
        var initialOffset = profileMain.Bounds.Width;
        Assert.InRange(translation.X, initialOffset - 1, initialOffset + 1);

        try
        {
            var addProfileRow = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("managerAddRow"));
            addProfileRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await AvaloniaTestWait.PropertyAsync(
                translation,
                TranslateTransform.XProperty,
                () => translation.IsAnimating(TranslateTransform.XProperty),
                "compact profile creator opening slide to start");
            await AvaloniaTestWait.UntilAsync(
                () => Math.Abs(translation.X) <= 0.01,
                "compact profile creator opening slide to finish");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ProfileRefreshDoesNotSnapNavigationPaneDuringWizardClose()
    {
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(
        [
            new Profile { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() }
        ]);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active");

        using var profilesViewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));
        var view = new ProfilesView { DataContext = profilesViewModel };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        var pane = Assert.IsType<Border>(view.FindControl<Border>("ProfilesListPane"));
        Assert.Equal(276, pane.Bounds.Width);

        profilesViewModel.ShowCreateChoiceCommand.Execute(null);
        await AvaloniaTestWait.UntilAsync(
            () => pane.Bounds.Width <= 0.5,
            "navigation pane to finish hiding before wizard close");

        profilesViewModel.CancelCreationCommand.Execute(null);
        profileRepository.Raise(repository => repository.ProfilesChanged += null);
        await AvaloniaTestWait.PropertyAsync(
            pane,
            Visual.OpacityProperty,
            () => pane.IsAnimating(Visual.OpacityProperty),
            "navigation pane to start reopening");
        Assert.False(pane.IsAnimating(Layoutable.WidthProperty));
        Assert.Equal(276, pane.Bounds.Width);

        await AvaloniaTestWait.UntilAsync(
            () => !pane.IsAnimating(Visual.OpacityProperty),
            "navigation pane to finish reopening");
        Assert.Equal(276, pane.Bounds.Width);
        Assert.Equal(1, pane.Opacity);

        window.Close();
    }
}
