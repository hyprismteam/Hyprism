// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Globalization;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Labs.Lottie;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Desktop.Integrations.GitHub;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Screens.Profiles;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Shell;
using Hyprism.Core.Models;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Accounts;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class MainWindowRenderTests
{
    [AvaloniaFact]
    public async Task InstanceWizardPopupStaysInsideTheMainSceneSurface()
    {
        using var httpClient = new HttpClient();
        using var viewModel = MainWindowViewModelFactory.Create(httpClient);
        var window = new MainWindow
        {
            Width = 1024,
            Height = 700,
            DataContext = viewModel
        };

        window.Show();
        viewModel.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();

        var instancesView = Assert.Single(window.GetVisualDescendants().OfType<InstancesView>());
        var addInstanceRow = Assert.Single(
            instancesView.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("managerAddRow"));
        addInstanceRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var wizard = instancesView.FindControl<Border>("InstanceCreatorScreen");
        Assert.NotNull(wizard);
        await WaitForConditionAsync(
            () => wizard!.IsEffectivelyVisible,
            "instance creator wizard to open");

        foreach (var version in new[] { 7, 6, 5, 4, 3 })
        {
            viewModel.Instances.AvailableInstanceVersions.Add(
                new InstanceVersionItemViewModel(version, version == 7));
        }
        Dispatcher.UIThread.RunJobs();

        var creatorView = Assert.IsType<InstanceCreatorView>(
            instancesView.FindControl<InstanceCreatorView>("InstanceCreatorContentView"));
        var comboBox = creatorView.FindControl<FadingComboBox>("InstanceVersionComboBox");
        Assert.NotNull(comboBox);
        comboBox!.Margin = new Thickness(0, 32, 0, 0);
        Dispatcher.UIThread.RunJobs();
        comboBox.IsDropDownOpen = true;
        var popup = Assert.Single(comboBox.GetVisualDescendants().OfType<FadingPopup>());
        await WaitForConditionAsync(
            () => popup.Child is Visual { Bounds.Height: > 0 },
            "instance version popup to measure");

        var surface = Assert.IsType<Border>(window.FindControl<Border>("MainSceneSurface"));
        var popupChild = Assert.IsAssignableFrom<Visual>(popup.Child);
        var popupOrigin = popupChild.TranslatePoint(new Point(), window);
        var surfaceOrigin = surface.TranslatePoint(new Point(), window);
        var clip = Assert.IsType<RectangleGeometry>(popupChild.Clip).Rect;
        Assert.NotNull(popupOrigin);
        Assert.NotNull(surfaceOrigin);
        Assert.True(
            popupOrigin!.Value.Y + clip.Bottom <=
            surfaceOrigin!.Value.Y + surface.Bounds.Height + 0.5);

        window.Close();
    }

    [AvaloniaFact]
    public void WizardActionsUseSharedSeparatedAppearance()
    {
        var secondaryAction = new Button { Content = "Back" };
        secondaryAction.Classes.Add("wizardAction");
        var primaryAction = new Button { Content = "Continue" };
        primaryAction.Classes.Add("wizardAction");
        primaryAction.Classes.Add("create");
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                secondaryAction,
                primaryAction
            }
        };
        var window = new Window
        {
            Width = 400,
            Height = 200,
            Content = actions
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.InRange(secondaryAction.Bounds.Width, 111.5, 112.5);
        Assert.InRange(primaryAction.Bounds.Width, 149.5, 150.5);
        Assert.InRange(secondaryAction.Bounds.Height, 49.5, 50.5);
        Assert.InRange(primaryAction.Bounds.Height, 49.5, 50.5);
        Assert.InRange(primaryAction.Bounds.X - secondaryAction.Bounds.Right, 9.5, 10.5);
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(secondaryAction.Background).Color.A);
        Assert.True(
            Assert.IsAssignableFrom<ISolidColorBrush>(primaryAction.Background).Color.A > 0);

        var secondaryPoint = secondaryAction.TranslatePoint(
            new Point(secondaryAction.Bounds.Width / 2, secondaryAction.Bounds.Height / 2),
            window);
        Assert.NotNull(secondaryPoint);
        window.MouseMove(secondaryPoint!.Value);
        Dispatcher.UIThread.RunJobs();

        Assert.True(secondaryAction.IsPointerOver);
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(secondaryAction.Background).Color.A);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ProfilesViewUsesInstanceStyleCardsMenusAndWizardScreen()
    {
        var profiles = new List<Profile>
        {
            new()
            {
                Id = "active-profile",
                Name = "ActivePlayer",
                UUID = Guid.NewGuid().ToString()
            },
            new()
            {
                Id = "second-profile",
                Name = "SecondPlayer",
                UUID = Guid.NewGuid().ToString()
            }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(profiles);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active-profile");

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        var view = new ProfilesView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());

        var cards = view.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("managerListItem"))
            .ToArray();
        var profilesListPane = Assert.IsType<Border>(view.FindControl<Border>("ProfilesListPane"));
        Assert.Equal(2, cards.Length);
        var selectedProfile = viewModel.SelectedProfile;
        var secondProfileRow = Assert.Single(
            cards,
            button => button.DataContext is ProfileItemViewModel { Id: "second-profile" });
        Assert.Same(viewModel.SelectProfileCommand, secondProfileRow.Command);
        Assert.Same(viewModel.Profiles[1], secondProfileRow.CommandParameter);
        secondProfileRow.Command!.Execute(secondProfileRow.CommandParameter);
        Assert.Same(viewModel.Profiles[1], viewModel.SelectedProfile);
        viewModel.SelectProfileCommand.Execute(selectedProfile);
        Assert.All(cards, card => Assert.Equal(new Thickness(0), card.BorderThickness));
        Assert.All(
            cards,
            card =>
            {
                var origin = card.TranslatePoint(default, profilesListPane);
                Assert.NotNull(origin);
                Assert.Equal(14, origin.Value.X, 3);
                Assert.Equal(
                    14,
                    profilesListPane.Bounds.Width - origin.Value.X - card.Bounds.Width,
                    3);
            });
        Assert.Equal(2, view.GetVisualDescendants()
            .OfType<Border>()
            .Count(border => border.IsEffectivelyVisible && border.Classes.Contains("managerListDragTarget")));

        var menuTargets = view.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("profileMoreTarget"))
            .ToArray();
        Assert.Equal(2, menuTargets.Length);
        Assert.DoesNotContain(
            view.GetVisualDescendants().OfType<Border>(),
            border => border.IsEffectivelyVisible && border.Classes.Contains("profileListAvatar"));
        viewModel.Profiles[0].IsMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.Profiles[0].IsMenuOpen);
        viewModel.Profiles[0].IsMenuOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.Profiles[0].IsMenuOpen);

        Assert.All(
            view.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("profileAvatar")),
            avatar => Assert.Equal(new Thickness(0), avatar.BorderThickness));
        Assert.All(
            view.GetVisualDescendants()
                .OfType<StackPanel>()
                .Where(panel => panel.Classes.Contains("managerDeleteActionContent")),
            panel => Assert.Equal(HorizontalAlignment.Center, panel.HorizontalAlignment));
        Assert.All(
            view.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("deleteAction")),
            button => Assert.Equal(new Thickness(0), button.Padding));

        var profileInfoGroup = view.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("managerInfoGroup"));
        var profileInfoCells = profileInfoGroup.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("managerInfoCell"))
            .ToArray();
        Assert.Equal(4, profileInfoCells.Length);
        Assert.All(
            profileInfoCells,
            cell => Assert.Equal(VerticalAlignment.Center, cell.Child?.VerticalAlignment));
        Assert.DoesNotContain(
            profileInfoGroup.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "ActivePlayer");

        var previewPath = Environment.GetEnvironmentVariable("HYPRISM_PROFILES_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            var directory = Path.GetDirectoryName(previewPath)!;
            var stem = Path.GetFileNameWithoutExtension(previewPath);
            window.CaptureRenderedFrame()!.Save(
                Path.Combine(directory, $"{stem}-wide.png"),
                PngBitmapEncoderOptions.Default);

            var activeProfile = viewModel.SelectedProfile;
            viewModel.SelectProfileCommand.Execute(
                viewModel.Profiles.Single(profile => !profile.IsActive));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(
                Path.Combine(directory, $"{stem}-inactive-wide.png"),
                PngBitmapEncoderOptions.Default);
            viewModel.SelectProfileCommand.Execute(activeProfile);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        var profileOverview = view.FindControl<Grid>("ProfileOverview");
        var profileEditorContent = view.FindControl<Grid>("ProfileEditorContent");
        var profileMain = view.FindControl<Grid>("ProfileMain");
        var profileMainWidthWithList = profileMain!.Bounds.Width;
        var wizard = Assert.IsType<Border>(view.FindControl<Border>("ProfileCreatorScreen"));
        var profileWizardReveal = Assert.IsType<WizardRevealIcon>(
            view.FindControl<WizardRevealIcon>("ProfileWizardReveal"));
        var profileWizardAnimation = profileWizardReveal.Animation;
        var creatorOpened = false;
        TaskCompletionSource<bool>? profileAnimationCompleted = null;
        EventHandler? profileAnimationCompletedHandler = null;
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            profileAnimationCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            profileAnimationCompletedHandler = (_, _) =>
            {
                if (creatorOpened)
                    profileAnimationCompleted.TrySetResult(true);
            };
            profileWizardAnimation.AnimationCompleted += profileAnimationCompletedHandler;
        }
        var profileListHidden = WaitForAvaloniaPropertyAsync(
            profilesListPane!,
            Visual.BoundsProperty,
            () => profilesListPane.Bounds.Width <= 0.5,
            "profiles list width to collapse");
        var wizardOpened = WaitForAvaloniaPropertyAsync(
            wizard,
            Visual.OpacityProperty,
            () => wizard.IsEffectivelyVisible && wizard.Opacity >= 0.99,
            "profile creator to finish opening");
        creatorOpened = true;
        viewModel.ShowCreateChoiceCommand.Execute(null);
        Assert.True(profileOverview!.IsVisible);
        Assert.True(profileEditorContent!.IsVisible);
        Assert.True(viewModel.IsProfileEditorVisible);
        await Task.WhenAll(profileListHidden, wizardOpened);
        Dispatcher.UIThread.RunJobs();
        Assert.True(wizard.IsEffectivelyVisible);
        Assert.InRange(profilesListPane.Bounds.Width, 0, 0.5);
        Assert.Equal(0, profilesListPane.Opacity);
        Assert.False(profilesListPane.IsHitTestVisible);
        Assert.True(profileMain.Bounds.Width > profileMainWidthWithList + 275);
        Assert.Contains("wizardScreen", wizard.Classes);
        Assert.Equal("/Assets/Lotties/avatar-reveal.json", profileWizardAnimation.Path);
        Assert.True(profileWizardAnimation.AutoPlay);
        Assert.Equal(2, profileWizardAnimation.PlayBackRate);
        Assert.Equal(1, profileWizardAnimation.RepeatCount);
        Assert.NotNull(profileWizardAnimation.OpacityMask);
        Assert.Equal(64, profileWizardAnimation.Width);
        Assert.Equal(64, profileWizardAnimation.Height);
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            var directory = Path.GetDirectoryName(previewPath)!;
            var stem = Path.GetFileNameWithoutExtension(previewPath);
            window.CaptureRenderedFrame()!.Save(
                Path.Combine(directory, $"{stem}-wizard.png"),
                PngBitmapEncoderOptions.Default);
            await profileAnimationCompleted!.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            window.CaptureRenderedFrame()!.Save(
                Path.Combine(directory, $"{stem}-wizard-final.png"),
                PngBitmapEncoderOptions.Default);
            profileWizardAnimation.AnimationCompleted -= profileAnimationCompletedHandler;
        }

        var profileCreationChoice = view.FindControl<StackPanel>("ProfileCreationChoiceContent");
        var offlineProfileCreation = view.FindControl<StackPanel>("OfflineProfileCreationContent");
        var offlineProfileStepActivated = WaitForAvaloniaPropertyAsync(
            profileWizardReveal,
            WizardRevealIcon.AnimationPathProperty,
            () => profileWizardAnimation.Path == "/Assets/Lotties/avatar-jumping.json",
            "offline profile step to become active");
        view.FindControl<Button>("BeginOfflineProfileCreationButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(profileCreationChoice!.IsVisible);
        Assert.False(offlineProfileCreation!.IsVisible);
        Assert.Equal("/Assets/Lotties/avatar-reveal.json", profileWizardAnimation.Path);
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            var directory = Path.GetDirectoryName(previewPath)!;
            var stem = Path.GetFileNameWithoutExtension(previewPath);
            window.CaptureRenderedFrame()!.Save(
                Path.Combine(directory, $"{stem}-wizard-step-transition.png"),
                PngBitmapEncoderOptions.Default);
        }
        await offlineProfileStepActivated;
        Dispatcher.UIThread.RunJobs();
        Assert.False(profileCreationChoice.IsVisible);
        Assert.True(offlineProfileCreation.IsVisible);
        Assert.Equal("/Assets/Lotties/avatar-jumping.json", profileWizardAnimation.Path);

        var profileCreationChoiceActivated = WaitForAvaloniaPropertyAsync(
            profileWizardReveal,
            WizardRevealIcon.AnimationPathProperty,
            () => profileWizardAnimation.Path == "/Assets/Lotties/avatar-reveal.json",
            "profile creation choice to become active");
        view.FindControl<Button>("OfflineProfileCreationBackButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(profileCreationChoice.IsVisible);
        Assert.True(offlineProfileCreation.IsVisible);
        await profileCreationChoiceActivated;
        Dispatcher.UIThread.RunJobs();
        Assert.True(profileCreationChoice.IsVisible);
        Assert.False(offlineProfileCreation.IsVisible);
        Assert.Equal("/Assets/Lotties/avatar-reveal.json", profileWizardAnimation.Path);
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            var directory = Path.GetDirectoryName(previewPath)!;
            var stem = Path.GetFileNameWithoutExtension(previewPath);
            window.CaptureRenderedFrame()!.Save(
                Path.Combine(directory, $"{stem}-wizard-after-back.png"),
                PngBitmapEncoderOptions.Default);
        }

        offlineProfileStepActivated = WaitForAvaloniaPropertyAsync(
            profileWizardReveal,
            WizardRevealIcon.AnimationPathProperty,
            () => profileWizardAnimation.Path == "/Assets/Lotties/avatar-jumping.json",
            "offline profile step to become active again");
        view.FindControl<Button>("BeginOfflineProfileCreationButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await offlineProfileStepActivated;
        Dispatcher.UIThread.RunJobs();
        Assert.False(profileCreationChoice.IsVisible);
        Assert.True(offlineProfileCreation.IsVisible);

        var profileListWidthRestored = WaitForAvaloniaPropertyAsync(
            profilesListPane,
            Visual.BoundsProperty,
            () => profilesListPane.Bounds.Width >= 275.5,
            "profiles list width to be restored");
        var profileListOpacityRestored = WaitForAvaloniaPropertyAsync(
            profilesListPane,
            Visual.OpacityProperty,
            () => profilesListPane.Opacity >= 0.99,
            "profiles list opacity to be restored");
        viewModel.CancelCreationCommand.Execute(null);
        await Task.WhenAll(profileListWidthRestored, profileListOpacityRestored);
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(profilesListPane.Bounds.Width, 275.5, 276.5);
        Assert.InRange(profilesListPane.Opacity, 0.99, 1);
        Assert.True(profilesListPane.IsHitTestVisible);
        var wizardTranslation = Assert.IsType<TranslateTransform>(wizard.RenderTransform);
        var wizardReopened = WaitForAvaloniaPropertyAsync(
            wizard,
            Visual.OpacityProperty,
            () => wizard.IsEffectivelyVisible && wizard.Opacity >= 0.99,
            "profile creator to finish reopening");
        var wizardTranslationReset = WaitForAvaloniaPropertyAsync(
            wizardTranslation,
            TranslateTransform.XProperty,
            () => Math.Abs(wizardTranslation.X) < 0.01,
            "profile creator translation to reset");
        viewModel.ShowCreateChoiceCommand.Execute(null);
        await Task.WhenAll(wizardReopened, wizardTranslationReset);
        Dispatcher.UIThread.RunJobs();

        Assert.True(profileCreationChoice.IsEffectivelyVisible);
        Assert.Equal(1, profileCreationChoice.Opacity);
        Assert.Equal(
            0,
            Assert.IsType<TranslateTransform>(profileCreationChoice.RenderTransform).X);

        var wizardClosed = WaitForAvaloniaPropertyAsync(
            wizard,
            Visual.IsVisibleProperty,
            () => !wizard.IsVisible,
            "profile creator to finish closing");
        viewModel.CancelCreationCommand.Execute(null);
        await wizardClosed;
        window.Width = 760;
        Dispatcher.UIThread.RunJobs();
        var activeCard = view.GetVisualDescendants()
            .OfType<Button>()
            .First(button => button.Classes.Contains("managerListItem"));
        activeCard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForConditionAsync(
            () => view.Classes.Contains("compact") &&
                  view.FindControl<Border>("CompactProfilesToolbar")!.IsEffectivelyVisible,
            "compact profiles detail to open");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("compact", view.Classes);
        Assert.True(view.FindControl<Border>("CompactProfilesToolbar")!.IsEffectivelyVisible);
        var compactPrimaryAction = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Classes.Contains("managerCompactActionPart") &&
                              button.Classes.Contains("main"));
        Assert.Equal(126, compactPrimaryAction.Width);
        Assert.Contains("active", compactPrimaryAction.Classes);
        Assert.Equal(0, compactPrimaryAction.GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(panel => panel.Classes.Contains("profileActivationIdle"))
            .Opacity);
        Assert.Equal(1, compactPrimaryAction.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Classes.Contains("profileActivationActive"))
            .Opacity);

        var compactProfileTranslation = Assert.IsType<TranslateTransform>(
            view.FindControl<Grid>("ProfileMain")!.RenderTransform);
        Assert.True(view.TryCloseCompactContent());
        await WaitForConditionAsync(
            () => compactProfileTranslation.X > 0,
            "compact profiles detail to close");
        Dispatcher.UIThread.RunJobs();
        Assert.True(compactProfileTranslation.X > 0);
        var addProfileRow = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("managerAddRow"));
        addProfileRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(wizard.IsVisible);
        Assert.False(view.FindControl<Grid>("ProfileOverview")!.IsVisible);
        await WaitForConditionAsync(
            () => Math.Abs(compactProfileTranslation.X) < 0.01 && wizard.IsEffectivelyVisible,
            "compact profile creator to open");
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsCreationVisible);
        Assert.InRange(Math.Abs(compactProfileTranslation.X), 0, 0.01);
        Assert.True(wizard.IsEffectivelyVisible);
        Assert.False(view.FindControl<Grid>("ProfileOverview")!.IsVisible);

        viewModel.CancelCreationCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(wizard.IsVisible);
        Assert.True(view.FindControl<StackPanel>("ProfileCreationChoiceContent")!.IsVisible);
        await WaitForConditionAsync(
            () => compactProfileTranslation.X > 0 &&
                  !wizard.IsVisible &&
                  view.FindControl<Grid>("ProfileOverview")!.IsVisible,
            "compact profile creator to close");
        Dispatcher.UIThread.RunJobs();
        Assert.True(compactProfileTranslation.X > 0);
        Assert.False(wizard.IsVisible);
        Assert.True(view.FindControl<Grid>("ProfileOverview")!.IsVisible);
        Assert.False(viewModel.IsCreateChoiceVisible);

        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            var directory = Path.GetDirectoryName(previewPath)!;
            var stem = Path.GetFileNameWithoutExtension(previewPath);
            window.CaptureRenderedFrame()!.Save(
                Path.Combine(directory, $"{stem}-compact.png"),
                PngBitmapEncoderOptions.Default);
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task OfficialProfileWizardUsesSeparateOverlappingSignInAction()
    {
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var authenticator = new Mock<IHytaleAuthenticator>();
        var authenticationStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        profileRepository.Setup(repository => repository.GetProfiles()).Returns([]);
        authenticator.Setup(service => service.LoginAsync(
                It.IsAny<AuthUriPresenter>(),
                It.IsAny<CancellationToken>()))
            .Returns<AuthUriPresenter, CancellationToken>(async (_, cancellationToken) =>
            {
                authenticationStarted.TrySetResult(cancellationToken);
                var authenticationCompletion = new TaskCompletionSource<HytaleAuthSession?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(
                    () => authenticationCompletion.TrySetCanceled(cancellationToken));
                return await authenticationCompletion.Task;
            });

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"),
            authenticator.Object);
        var view = new ProfilesView { DataContext = viewModel };
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        var creator = view.FindControl<Border>("ProfileCreatorScreen")!;
        var creatorOpened = WaitForAvaloniaPropertyAsync(
            creator,
            Visual.OpacityProperty,
            () => creator.IsEffectivelyVisible && creator.Opacity >= 0.99,
            "official profile wizard to finish opening");
        viewModel.ShowCreateChoiceCommand.Execute(null);
        await creatorOpened;
        Dispatcher.UIThread.RunJobs();

        var initialCancelButton = view.FindControl<Button>("ProfileCreationCancelButton")!;
        Assert.Contains("wizardAction", initialCancelButton.Classes);
        Assert.InRange(initialCancelButton.Bounds.Width, 111.5, 112.5);
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(initialCancelButton.Background).Color.A);

        var profileWizardReveal = view.FindControl<WizardRevealIcon>("ProfileWizardReveal")!;
        var officialContentActivated = WaitForAvaloniaPropertyAsync(
            profileWizardReveal,
            WizardRevealIcon.AnimationPathProperty,
            () => profileWizardReveal.Animation.Path == "/Assets/Lotties/avatar-looking.json",
            "official profile sign-in step to become active");
        view.FindControl<Button>("BeginOfficialProfileCreationButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await officialContentActivated;
        Dispatcher.UIThread.RunJobs();

        var signInButton = view.FindControl<Button>("OfficialProfileSignInButton")!;
        var backButton = view.FindControl<Button>("OfficialProfileCreationBackButton")!;
        var actions = view.FindControl<Grid>("OfficialProfileSignInActions")!;
        await WaitForAvaloniaPropertyAsync(
            signInButton,
            Visual.BoundsProperty,
            () => signInButton.IsEffectivelyVisible && signInButton.Bounds.Width > 0,
            "official profile sign-in action to finish layout");
        Dispatcher.UIThread.RunJobs();
        var progress = Assert.Single(
            signInButton.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Classes.Contains("managedActionProgress"));
        var spinner = Assert.Single(
            progress.Children.OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("managedActionSpinner"));
        var cancelLabel = Assert.Single(
            signInButton.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Classes.Contains("managedActionCancel"));
        var idleLabel = Assert.Single(
            signInButton.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "Sign in");

        Assert.True(signInButton.IsEffectivelyVisible);
        Assert.True(backButton.IsEffectivelyVisible);
        Assert.Equal("Sign in", idleLabel.Text);
        Assert.InRange(signInButton.Bounds.Width, 149.5, 150.5);
        Assert.InRange(backButton.Bounds.Width, 111.5, 112.5);
        Assert.InRange(backButton.Bounds.X, 0, 0.5);
        Assert.InRange(signInButton.Bounds.X, 121.5, 122.5);
        Assert.InRange(signInButton.Bounds.X - backButton.Bounds.Right, 9.5, 10.5);
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(backButton.Background).Color.A);

        var backActionPoint = backButton.TranslatePoint(
            new Point(backButton.Bounds.Width / 2, backButton.Bounds.Height / 2),
            window);
        Assert.NotNull(backActionPoint);
        window.MouseMove(backActionPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(backButton.IsPointerOver);
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(backButton.Background).Color.A);

        var initialActionPoint = signInButton.TranslatePoint(
            new Point(signInButton.Bounds.Width / 2, signInButton.Bounds.Height / 2),
            window);
        Assert.NotNull(initialActionPoint);
        window.MouseMove(initialActionPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(signInButton.IsPointerOver);

        var actionExpanded = WaitForAvaloniaPropertyAsync(
            signInButton,
            Visual.BoundsProperty,
            () => signInButton.Bounds.Width >= 271.5,
            "official sign-in action to finish expanding");
        var progressVisible = WaitForAvaloniaPropertyAsync(
            progress,
            Visual.OpacityProperty,
            () => progress.Opacity >= 0.99,
            "official sign-in spinner to finish appearing");
        var authentication = viewModel.SignInWithHytaleCommand.ExecuteAsync(null);
        var cancellationToken = await authenticationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.WhenAll(actionExpanded, progressVisible);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("active", signInButton.Classes);
        Assert.DoesNotContain("cancelArmed", signInButton.Classes);
        Assert.True(spinner.IsEffectivelyVisible);
        Assert.InRange(progress.Opacity, 0.99, 1);
        Assert.Equal(0, cancelLabel.Opacity);
        Assert.InRange(signInButton.Bounds.Width, 271.5, 272.5);
        Assert.InRange(signInButton.Bounds.X, 0, 0.5);
        Assert.InRange(backButton.Bounds.Width, 111.5, 112.5);
        Assert.True(backButton.IsEffectivelyVisible);
        Assert.True(signInButton.Bounds.Contains(backButton.Bounds));
        Assert.Equal(actions.Bounds.Width, signInButton.Bounds.Width, 0.5);

        window.MouseDown(initialActionPoint.Value, MouseButton.Left);
        window.MouseUp(initialActionPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(cancellationToken.IsCancellationRequested);

        window.MouseMove(new Point(window.Bounds.Width / 2, window.Bounds.Height - 8));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("cancelArmed", signInButton.Classes);

        var cancelVisible = WaitForAvaloniaPropertyAsync(
            cancelLabel,
            Visual.OpacityProperty,
            () => cancelLabel.Opacity >= 0.99,
            "official sign-in cancel action to finish appearing");
        var expandedActionPoint = signInButton.TranslatePoint(
            new Point(signInButton.Bounds.Width / 2, signInButton.Bounds.Height / 2),
            window);
        Assert.NotNull(expandedActionPoint);
        window.MouseMove(expandedActionPoint!.Value);
        await cancelVisible;
        Dispatcher.UIThread.RunJobs();

        Assert.True(signInButton.IsPointerOver);
        Assert.Equal(
            Color.Parse("#D83B45"),
            Assert.IsAssignableFrom<ISolidColorBrush>(signInButton.Background).Color);
        Assert.InRange(cancelLabel.Opacity, 0.99, 1);

        var previewPath = Environment.GetEnvironmentVariable("HYPRISM_PROFILE_SIGN_IN_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            var previewDirectory = Path.GetDirectoryName(previewPath);
            if (!string.IsNullOrWhiteSpace(previewDirectory))
                Directory.CreateDirectory(previewDirectory);

            window.CaptureRenderedFrame()!.Save(previewPath, PngBitmapEncoderOptions.Default);
        }

        window.MouseDown(expandedActionPoint.Value, MouseButton.Left);
        window.MouseUp(expandedActionPoint.Value, MouseButton.Left);
        await authentication.WaitAsync(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.False(viewModel.IsAuthenticating);
        Assert.False(viewModel.IsAuthenticationCancellationArmed);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ExternalUriLauncherRejectsUnsupportedSchemesBeforeResolvingWindow()
    {
        var topLevelRequested = false;
        var launcher = new ExternalUriLauncher(() =>
        {
            topLevelRequested = true;
            return null;
        });

        var launched = await launcher.LaunchAsync(new Uri("file:///tmp/hyprism"));
        var openedDirectory = await launcher.LaunchDirectoryAsync("relative/path");

        Assert.False(launched);
        Assert.False(openedDirectory);
        Assert.False(topLevelRequested);
    }

    [Fact]
    public void ExternalUriLauncherPreservesEscapedOAuthUriAsOneArgument()
    {
        var uri = new Uri(
            "https://oauth.accounts.hytale.com/oauth2/auth" +
            "?scope=openid%20offline%20auth%3Alauncher&state=VALUE%3D");

        var startInfo = ExternalUriLauncher.CreateBrowserStartInfo(uri);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(uri.AbsoluteUri, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
            return;
        }

        Assert.Single(startInfo.ArgumentList);
        Assert.Equal(uri.AbsoluteUri, startInfo.ArgumentList[0]);
        Assert.Contains("scope=openid%20offline%20auth%3Alauncher", startInfo.ArgumentList[0]);
        Assert.DoesNotContain("scope=openid offline", startInfo.ArgumentList[0]);
        Assert.Equal(OperatingSystem.IsMacOS() ? "open" : "xdg-open", startInfo.FileName);
    }

    [AvaloniaFact]
    public async Task NewsMediaViewModelsDecodeBlockAndInlineImages()
    {
        using var client = new HttpClient(new TinyPngHandler());
        var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "image",
                ImageUrl = "https://cdn.hytale.com/gallery.png"
            }
        ]));
        using var inline = new NewsInlineImageViewModel(new NewsContentNode
        {
            Kind = "inline-image",
            ImageUrl = "https://cdn.hytale.com/emotes/heart.png",
            ImagePresentation = "emote"
        });

        await block.LoadImageAsync(client, CancellationToken.None);
        await inline.LoadAsync(client, CancellationToken.None);

        Assert.True(block.HasImage);
        Assert.NotNull(inline.Image);
        block.Dispose();
    }

    [Fact]
    public void MixedMediaContainerKeepsInlineContentInOneParagraph()
    {
        var blocks = NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "container",
                Children =
                [
                    new NewsContentNode
                    {
                        Kind = "bold",
                        Children =
                        [new NewsContentNode { Kind = "text", Text = "Arcane Power" }]
                    },
                    new NewsContentNode { Kind = "text", Text = " (by Tayko_Dev)" },
                    new NewsContentNode { Kind = "line-break" },
                    new NewsContentNode
                    {
                        Kind = "link",
                        Url = "https://www.curseforge.com/hytale/mods/arcane-power",
                        Children =
                        [new NewsContentNode
                        {
                            Kind = "text",
                            Text = "https://www.curseforge.com/hytale/mods/arcane-power"
                        }]
                    },
                    new NewsContentNode { Kind = "line-break" },
                    new NewsContentNode
                    {
                        Kind = "image",
                        ImageUrl = "https://cdn.hytale.com/arcane-power.png"
                    }
                ]
            }
        ]);

        try
        {
            Assert.Equal(2, blocks.Count);
            Assert.True(blocks[0].IsParagraph);
            Assert.Contains(blocks[0].Nodes, node => node.Kind == "bold");
            Assert.Contains(blocks[0].Nodes, node => node.Kind == "link");
            Assert.True(blocks[1].IsImage);
            Assert.DoesNotContain(blocks, block => block.Kind is "bold" or "link" or "line-break");
        }
        finally
        {
            foreach (var block in blocks)
                block.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ArticleReleasesDecodedImagesWithoutDiscardingParsedContent()
    {
        using var client = new HttpClient(new TinyPngHandler());
        using var article = new NewsArticleViewModel(
            new NewsArticleResponse
            {
                Title = "Image article",
                Url = "https://hytale.com/news/image-article",
                Content =
                [
                    new NewsContentNode
                    {
                        Kind = "image",
                        ImageUrl = "https://cdn.hytale.com/gallery.png"
                    },
                    new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "inline-image",
                                ImageUrl = "https://cdn.hytale.com/emotes/heart.png",
                                ImagePresentation = "emote"
                            }
                        ]
                    }
                ]
            },
            Mock.Of<IExternalUriLauncher>());

        await article.LoadImagesAsync(client, CancellationToken.None);

        Assert.True(article.Blocks[0].HasImage);
        Assert.NotNull(Assert.Single(article.Blocks[1].InlineImages).Image);

        article.ReleaseImages();

        Assert.False(article.Blocks[0].HasImage);
        Assert.Null(Assert.Single(article.Blocks[1].InlineImages).Image);
        Assert.Equal(2, article.Blocks.Count);
    }

    [AvaloniaFact]
    public async Task ArticleRenderingYieldsToInputBeforeRevealingStableContent()
    {
        using var article = new NewsArticleViewModel(
            new NewsArticleResponse
            {
                Title = "Long article",
                Url = "https://hytale.com/news/long-article",
                Content = Enumerable.Range(0, 25)
                    .Select(index => new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "text",
                                Text = $"Paragraph {index}"
                            }
                        ]
                    })
                    .ToList()
            },
            Mock.Of<IExternalUriLauncher>());
        var changes = new List<NotifyCollectionChangedEventArgs>();
        var blocksWhenInputRan = -1;
        article.RenderedBlocks.CollectionChanged += (_, args) =>
        {
            changes.Add(args);
            if (args.Action == NotifyCollectionChangedAction.Add && blocksWhenInputRan < 0)
            {
                Dispatcher.UIThread.Post(
                    () => blocksWhenInputRan = article.RenderedBlocks.Count,
                    DispatcherPriority.Input);
            }
        };
        var blocksWhenRevealed = -1;

        await article.PrepareForDisplayAsync(
            CancellationToken.None,
            () =>
            {
                blocksWhenRevealed = article.RenderedBlocks.Count;
                return Task.CompletedTask;
            });

        Assert.Equal(article.Blocks.Count, article.RenderedBlocks.Count);
        var additions = changes
            .Where(change => change.Action == NotifyCollectionChangedAction.Add)
            .ToArray();
        Assert.Equal([4, 4, 4, 4, 4, 4, 1], additions
            .Select(change => change.NewItems!.Count));
        Assert.Equal(4, blocksWhenInputRan);
        Assert.Equal(25, blocksWhenRevealed);
    }

    [AvaloniaTheory]
    [InlineData(1024)]
    [InlineData(1280)]
    public async Task ManagedInstanceActionAnimatesAndRemainsClickableForCancellation(int width)
    {
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var launchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var instance = new InstanceInfo
        {
            Id = "animated-action",
            Name = "Animated Action",
            Branch = "release",
            Version = 42,
            IsInstalled = true
        };

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id))
            .Returns("/tmp/hyprism-animated-action");
        instances.Setup(service => service.IsClientPresent(It.IsAny<string>())).Returns(true);
        profile.Setup(service => service.GetNick()).Returns("Action Test");
        launchCoordinator.Setup(service => service.LaunchAsync(
                instance.Id,
                It.IsAny<AuthUriPresenter?>()))
            .Returns(launchCompletion.Task);
        var gameRunning = false;
        gameProcess.Setup(service => service.IsInstanceRunning(instance.Id)).Returns(() => gameRunning);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"));
        var window = new MainWindow
        {
            Width = width,
            Height = 700,
            DataContext = viewModel
        };

        window.Show();
        viewModel.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();
        var instancesView = Assert.Single(window.GetVisualDescendants().OfType<InstancesView>());
        var compact = instancesView.Bounds.Width < 940;
        var primaryAction = compact
            ? instancesView.FindControl<InstanceOverviewView>("InstanceOverviewContentView")!
                .FindControl<Button>("CompactInstancePrimaryAction")!
            : instancesView.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("managerAction") &&
                                  button.Classes.Contains("primary"));
        var collapsingAction = compact
            ? instancesView.FindControl<InstanceOverviewView>("InstanceOverviewContentView")!
                .FindControl<Button>("CompactInstanceMoreButton")!
            : instancesView.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("deleteAction"));
        var collapsingEditAction = compact
            ? null
            : instancesView.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("editAction"));
        if (compact)
        {
            var instanceRow = instancesView.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("managerListItem"));
            instanceRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForConditionAsync(
                () => primaryAction.IsEffectivelyVisible && primaryAction.Bounds.Width > 0,
                "compact managed instance action to become visible");
            Dispatcher.UIThread.RunJobs();
        }

        var expectedPrimaryActionWidth = compact ? 126 : 150;
        var instancesContentTranslation = Assert.IsType<TranslateTransform>(
            instancesView.FindControl<Grid>("InstancesContent")!.RenderTransform);
        await WaitForConditionAsync(
            () => primaryAction.IsEffectivelyVisible &&
                  Math.Abs(primaryAction.Bounds.Width - expectedPrimaryActionWidth) <= 0.5 &&
                  Math.Abs(instancesContentTranslation.X) <= 0.01,
            "managed instance action layout to settle");

        var initialActionPoint = primaryAction.TranslatePoint(
            new Point(primaryAction.Bounds.Width / 2, primaryAction.Bounds.Height / 2),
            window);
        Assert.NotNull(initialActionPoint);
        window.MouseMove(initialActionPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(primaryAction.IsPointerOver);

        var launchOperation = viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        await WaitForConditionAsync(
            () => primaryAction.Classes.Contains("active") &&
                  collapsingAction.Bounds.Width <= 0.5 &&
                  (collapsingEditAction is null || collapsingEditAction.Bounds.Width <= 0.5),
            "managed instance action to enter progress state");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("active", primaryAction.Classes);
        Assert.DoesNotContain("cancelArmed", primaryAction.Classes);
        Assert.True(primaryAction.IsEffectivelyVisible);
        Assert.InRange(primaryAction.Bounds.Width, compact ? 215.5 : 437.5, compact ? 216.5 : 438.5);
        Assert.InRange(collapsingAction.Bounds.Width, 0, 0.5);
        if (collapsingEditAction is not null)
            Assert.InRange(collapsingEditAction.Bounds.Width, 0, 0.5);
        var progressContent = Assert.Single(
            primaryAction.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Classes.Contains("managedActionProgress"));
        var actionContent = Assert.IsType<Grid>(progressContent.Parent);
        Assert.Equal(compact ? 190 : 225, actionContent.Bounds.Width);
        Assert.Equal(1, progressContent.Opacity);
        Assert.Equal(new GridLength(48), progressContent.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(48), progressContent.ColumnDefinitions[2].Width);

        var status = Assert.Single(
            progressContent.GetVisualDescendants().OfType<FadingTextBlock>(),
            control => control.Classes.Contains("managedActionStatus"));
        var metric = Assert.Single(progressContent.Children.OfType<TextBlock>());
        var spinner = Assert.Single(
            progressContent.Children.OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("managedActionSpinner"));
        Assert.Equal(0, Grid.GetColumn(spinner));
        Assert.Equal(HorizontalAlignment.Center, spinner.HorizontalAlignment);
        Assert.Equal(20, spinner.Bounds.Width);
        Assert.Equal(20, spinner.Bounds.Height);
        Assert.True(spinner.Data!.Bounds.Left >= spinner.StrokeThickness / 2);
        Assert.True(spinner.Data.Bounds.Top >= spinner.StrokeThickness / 2);
        Assert.True(spinner.Data.Bounds.Right <= spinner.Bounds.Width - spinner.StrokeThickness / 2);
        Assert.True(spinner.Data.Bounds.Bottom <= spinner.Bounds.Height - spinner.StrokeThickness / 2);
        var spinnerRotation = spinner.RenderTransform is RotateTransform rotation
            ? rotation
            : Assert.Single(
                Assert.IsType<TransformGroup>(spinner.RenderTransform).Children
                    .OfType<RotateTransform>());
        await WaitForConditionAsync(
            () => spinnerRotation.Angle is > 1 and < 359,
            "managed instance spinner to advance");
        Assert.Equal(1, Grid.GetColumn(status));
        Assert.Equal(HorizontalAlignment.Stretch, status.HorizontalAlignment);
        Assert.Equal(2, Grid.GetColumn(metric));
        Assert.Equal(HorizontalAlignment.Right, metric.HorizontalAlignment);
        Assert.Equal(48, metric.Width);
        Assert.NotNull(metric.FontFeatures);
        Assert.Contains(metric.FontFeatures!, feature => feature.Tag == "tnum");
        var statusCenter = status.TranslatePoint(
            new Point(status.Bounds.Width / 2, status.Bounds.Height / 2),
            progressContent);
        Assert.NotNull(statusCenter);
        Assert.InRange(
            Math.Abs(statusCenter!.Value.X - progressContent.Bounds.Width / 2),
            0,
            0.5);

        var cancelLabel = Assert.Single(
            primaryAction.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Classes.Contains("managedActionCancel"));
        Assert.Equal(0, cancelLabel.Opacity);

        window.MouseDown(initialActionPoint.Value, MouseButton.Left);
        window.MouseUp(initialActionPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        gameSession.Verify(service => service.CancelDownload(instance.Id), Times.Never);

        window.MouseMove(new Point(window.Bounds.Width / 2, window.Bounds.Height - 8));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("cancelArmed", primaryAction.Classes);

        var actionPoint = primaryAction.TranslatePoint(
            new Point(primaryAction.Bounds.Width / 2, primaryAction.Bounds.Height / 2),
            window);
        Assert.NotNull(actionPoint);
        window.MouseMove(actionPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        await WaitForConditionAsync(
            () => primaryAction.Background is ISolidColorBrush { Color: var color } &&
                  color == Color.Parse("#D83B45") &&
                  cancelLabel.Opacity >= 0.99,
            "managed instance cancellation action to appear");
        Dispatcher.UIThread.RunJobs();
        Assert.True(primaryAction.IsPointerOver);
        Assert.Equal(
            Color.Parse("#D83B45"),
            Assert.IsAssignableFrom<ISolidColorBrush>(primaryAction.Background).Color);
        Assert.Equal(
            Colors.White,
            Assert.IsAssignableFrom<ISolidColorBrush>(primaryAction.Foreground).Color);
        Assert.InRange(cancelLabel.Opacity, 0.99, 1);

        window.MouseDown(actionPoint.Value, MouseButton.Left);
        window.MouseUp(actionPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        gameSession.Verify(service => service.CancelDownload(instance.Id), Times.Once);

        var spinnerCenter = spinner.TranslatePoint(
            new Point(spinner.Bounds.Width / 2, spinner.Bounds.Height / 2),
            progressContent);
        var metricBounds = metric.Bounds;
        Assert.NotNull(spinnerCenter);

        gameRunning = true;
        gameProcess.Raise(
            service => service.GameProcessStarted += null!,
            this,
            new GameProcessStartedEventArgs(new GameProcessInfo(
                123,
                DateTime.UtcNow,
                instance.Id,
                "profile-id",
                null,
                DateTime.UtcNow)));
        Dispatcher.UIThread.RunJobs();
        var runningIcon = Assert.Single(
            progressContent.Children.OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("running"));
        var runningIconCenter = runningIcon.TranslatePoint(
            new Point(runningIcon.Bounds.Width / 2, runningIcon.Bounds.Height / 2),
            progressContent);
        Assert.NotNull(runningIconCenter);
        Assert.InRange(Math.Abs(runningIconCenter!.Value.X - spinnerCenter!.Value.X), 0, 0.5);
        Assert.Equal(metricBounds, metric.Bounds);

        launchCompletion.SetResult();
        await launchOperation;
        Dispatcher.UIThread.RunJobs();
        var settledRunningIconCenter = runningIcon.TranslatePoint(
            new Point(runningIcon.Bounds.Width / 2, runningIcon.Bounds.Height / 2),
            progressContent);
        Assert.NotNull(settledRunningIconCenter);
        Assert.Equal(compact ? 190 : 225, actionContent.Bounds.Width);
        Assert.InRange(
            Math.Abs(settledRunningIconCenter!.Value.X - runningIconCenter.Value.X),
            0,
            0.5);
        Assert.Equal(metricBounds, metric.Bounds);

        gameRunning = false;
        gameProcess.Raise(
            service => service.GameProcessExited += null!,
            this,
            new GameProcessExitedEventArgs(new GameProcessInfo(
                123,
                DateTime.UtcNow,
                instance.Id,
                "profile-id",
                null,
                DateTime.UtcNow), 0));
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("emote", 24)]
    [InlineData("sticker", 64)]
    public void InlineNewsMediaUsesBlogRelativeSizing(string presentation, double expectedSize)
    {
        var node = new NewsContentNode
        {
            Kind = "inline-image",
            ImageUrl = $"https://cdn.hytale.com/emotes/{presentation}.png",
            AltText = $":{presentation}:",
            ImagePresentation = presentation
        };
        using var media = new NewsInlineImageViewModel(node);
        var control = new NewsRichTextBlock
        {
            InlineImages = [media],
            Nodes = [node]
        };
        Dispatcher.UIThread.RunJobs();

        var inline = Assert.IsType<InlineUIContainer>(Assert.Single(control.Inlines!));
        var image = Assert.IsType<Image>(inline.Child);
        Assert.Equal(BaselineAlignment.Center, inline.BaselineAlignment);
        Assert.Equal(expectedSize, image.Width);
        Assert.Equal(expectedSize, image.Height);
    }

    [AvaloniaFact]
    public void InlineCodeChipsKeepCompactMetricsInsideWrappedParagraphs()
    {
        var control = new NewsRichTextBlock
        {
            FontFamily = new FontFamily(
                "avares://Hyprism.Desktop/Assets/Fonts#Google Sans"),
            FontSize = 17,
            LineHeight = 28,
            TextWrapping = TextWrapping.Wrap,
            Nodes =
            [
                new NewsContentNode { Kind = "text", Text = "Added a " },
                new NewsContentNode { Kind = "inline-code", Text = "Trig" },
                new NewsContentNode
                {
                    Kind = "text",
                    Text = " density node to world generation. The new "
                },
                new NewsContentNode { Kind = "inline-code", Text = "\"Type\": \"Trig\"" },
                new NewsContentNode { Kind = "text", Text = " takes a " },
                new NewsContentNode { Kind = "inline-code", Text = "Function" },
                new NewsContentNode { Kind = "text", Text = " of " },
                new NewsContentNode { Kind = "inline-code", Text = "Sin" },
                new NewsContentNode { Kind = "text", Text = ", " },
                new NewsContentNode { Kind = "inline-code", Text = "Cos" },
                new NewsContentNode { Kind = "text", Text = ", or " },
                new NewsContentNode { Kind = "inline-code", Text = "Atan" },
                new NewsContentNode { Kind = "text", Text = " and an " },
                new NewsContentNode { Kind = "inline-code", Text = "InputScale" },
                new NewsContentNode { Kind = "text", Text = " before the function runs." }
            ]
        };
        var window = new Window
        {
            Width = 980,
            Height = 220,
            Background = new SolidColorBrush(Color.Parse("#0D0E10")),
            Content = new Border
            {
                Padding = new Thickness(28),
                Child = control
            }
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var chips = control.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("inlineCode"))
            .ToArray();
        Assert.Equal(7, chips.Length);
        Assert.All(chips, chip =>
        {
            Assert.Equal(new CornerRadius(6), chip.CornerRadius);
            Assert.InRange(chip.Bounds.Height, 18, 22);
            Assert.Equal(18, Assert.IsType<TextBlock>(chip.Child).LineHeight);
        });

        var previewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_INLINE_CODE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath))
            frame!.Save(previewPath, PngBitmapEncoderOptions.Default);

        window.Close();
    }

    [AvaloniaFact]
    public void StickerParagraphKeepsOnlyItsLeadBesideTheSticker()
    {
        var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "paragraph",
                Children =
                [
                    new NewsContentNode
                    {
                        Kind = "inline-image",
                        ImageUrl = "https://cdn.hytale.com/emotes/kweeb-wave.png",
                        ImagePresentation = "sticker"
                    },
                    new NewsContentNode { Kind = "text", Text = " Hello everyone! " },
                    new NewsContentNode { Kind = "line-break" },
                    new NewsContentNode { Kind = "text", Text = "Today, we want to shine a spotlight." }
                ]
            }
        ]));

        Assert.True(block.IsStickerParagraph);
        Assert.False(block.IsParagraph);
        Assert.NotNull(block.StickerImage);
        Assert.Equal("Hello everyone!", Assert.Single(block.StickerLeadNodes).Text?.Trim());
        Assert.Equal(
            "Today, we want to shine a spotlight.",
            Assert.Single(block.StickerBodyNodes).Text);

        block.Dispose();
    }

    [AvaloniaFact]
    public void StickerParagraphKeepsLeadTextBeforeTheSticker()
    {
        var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "paragraph",
                Children =
                [
                    new NewsContentNode { Kind = "text", Text = "Hey, everyone! " },
                    new NewsContentNode
                    {
                        Kind = "inline-image",
                        ImageUrl = "https://cdn.hytale.com/emotes/kweeb-wave.png",
                        ImagePresentation = "sticker"
                    }
                ]
            }
        ]));

        try
        {
            Assert.True(block.IsStickerParagraph);
            Assert.Equal("Hey, everyone!", Assert.Single(block.StickerLeadNodes).Text?.Trim());
            Assert.Empty(block.StickerBodyNodes);
        }
        finally
        {
            block.Dispose();
        }
    }

    [AvaloniaFact]
    public void ParagraphWrappedListItemDoesNotKeepAnEmptyTrailingLine()
    {
        var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "unordered-list",
                Children =
                [
                    new NewsContentNode
                    {
                        Kind = "list-item",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "paragraph",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "Compact patch note."
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ]));
        var item = Assert.Single(block.ListItems);
        var control = new NewsRichTextBlock { Nodes = item.Nodes };
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(control.Inlines);
        Assert.Equal("Compact patch note.", Assert.IsType<Run>(Assert.Single(control.Inlines!)).Text);

        block.Dispose();
    }

    [AvaloniaFact]
    public void ArticleLinkPreservesItsLeadingSpaceAndUsesTheBrowserCommand()
    {
        string? openedUrl = null;
        var command = new RelayCommand<string?>(url => openedUrl = url);
        var control = new NewsRichTextBlock
        {
            LinkCommand = command,
            Nodes =
            [
                new NewsContentNode { Kind = "text", Text = "Documentation: " },
                new NewsContentNode
                {
                    Kind = "link",
                    Url = "https://hytalemodding.dev/",
                    Children =
                    [
                        new NewsContentNode
                        {
                            Kind = "text",
                            Text = "https://hytalemodding.dev/"
                        }
                    ]
                }
            ]
        };
        Dispatcher.UIThread.RunJobs();

        var plainText = Assert.IsType<Run>(control.Inlines![0]);
        var link = Assert.IsType<Run>(control.Inlines[1]);
        var linkForeground = Assert.IsType<SolidColorBrush>(link.Foreground);
        var underline = Assert.IsType<SolidColorBrush>(Assert.Single(link.TextDecorations!).Stroke);

        Assert.Equal("Documentation: ", plainText.Text);
        Assert.Equal("https://hytalemodding.dev/", link.Text);
        Assert.Equal(Color.Parse("#C9BCFF"), linkForeground.Color);
        Assert.Equal(0, underline.Opacity);
        Assert.DoesNotContain(control.Inlines, inline => inline is InlineUIContainer);
        Assert.Null(openedUrl);
    }

    [Fact]
    public void YouTubeNodeCreatesASeparateClickableArticleBlock()
    {
        var command = new RelayCommand<string?>(_ => { });
        using var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "youtube",
                Url = "https://www.youtube.com/watch?v=amyYRSw3IZ0",
                ImageUrl = "https://i.ytimg.com/vi/amyYRSw3IZ0/hqdefault.jpg"
            }
        ], command));

        Assert.True(block.IsYouTube);
        Assert.False(block.IsImage);
        Assert.True(block.HasRemoteImages);
        Assert.Equal("https://www.youtube.com/watch?v=amyYRSw3IZ0", block.Url);
        Assert.Equal("https://i.ytimg.com/vi/amyYRSw3IZ0/hqdefault.jpg", block.ImageUrl);
        Assert.Same(command, block.LinkCommand);
    }

    [AvaloniaFact]
    public async Task CompactNewsLoadingUsesOpaqueReaderAndKeepsSkeletonBelowBack()
    {
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var articleCompletion = new TaskCompletionSource<NewsArticleResponse?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profile.Setup(service => service.GetNick()).Returns("Reader Test");
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new NewsItemResponse
                {
                    Title = "Uncached article",
                    Excerpt = "Loading without blocking the feed.",
                    Url = "https://hytale.com/news/2026/8/uncached-article",
                    Date = "2026-08-08",
                    Author = "Hytale Team"
                }
            ]);
        news.Setup(service => service.GetNewsArticleAsync(It.IsAny<string>()))
            .Returns(articleCompletion.Task);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"));
        var window = new MainWindow
        {
            Width = 1024,
            Height = 700,
            DataContext = viewModel
        };

        window.Show();
        viewModel.NavigateCommand.Execute("news");
        Dispatcher.UIThread.RunJobs();
        var openTask = viewModel.FeaturedNews!.OpenCommand.ExecuteAsync(null);
        await WaitForConditionAsync(
            () => viewModel.IsNewsArticleLoading &&
                  window.GetVisualDescendants()
                      .OfType<Border>()
                      .Any(border => border.IsEffectivelyVisible &&
                                     border.Classes.Contains("skeleton")),
            "compact news loading skeleton to become visible");
        Dispatcher.UIThread.RunJobs();

        var compactShell = FindVisualByName<Grid>(window, "CompactNewsShell");
        var articleHost = FindVisualByName<ContentControl>(window, "CompactArticleHost");
        Assert.NotNull(compactShell);
        Assert.NotNull(articleHost);
        Assert.True(viewModel.IsNewsArticleLoading);
        var articleTranslation = Assert.IsType<TranslateTransform>(articleHost!.RenderTransform);
        var articleTransition = Assert.IsType<DoubleTransition>(Assert.Single(
            articleTranslation.Transitions!,
            transition => transition is DoubleTransition { Property: { } property } &&
                          property == TranslateTransform.XProperty));
        Assert.Equal(TimeSpan.FromMilliseconds(300), articleTransition.Duration);
        Assert.IsType<CubicEaseInOut>(articleTransition.Easing);

        var readerRoot = articleHost!.GetVisualDescendants()
            .OfType<Grid>()
            .First(grid => grid.IsEffectivelyVisible && grid.Background is ISolidColorBrush);
        Assert.Equal(
            Color.Parse("#0D0E10"),
            Assert.IsAssignableFrom<ISolidColorBrush>(readerRoot.Background).Color);
        var back = articleHost.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("detailBack"));
        var skeleton = articleHost.GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.IsEffectivelyVisible && border.Classes.Contains("skeleton"));
        var backPosition = back.TranslatePoint(default, articleHost);
        var skeletonPosition = skeleton.TranslatePoint(default, articleHost);
        Assert.NotNull(backPosition);
        Assert.NotNull(skeletonPosition);
        Assert.True(skeletonPosition!.Value.Y >= backPosition!.Value.Y + back.Bounds.Height + 17);

        var loadingPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_COMPACT_LOADING_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(loadingPreviewPath))
        {
            var loadingFrame = window.CaptureRenderedFrame();
            Assert.NotNull(loadingFrame);
            loadingFrame!.Save(loadingPreviewPath, PngBitmapEncoderOptions.Default);
        }

        await WaitForConditionAsync(
            () => !viewModel.IsCompactNewsTransitionActive,
            "compact news page transition to complete");
        Dispatcher.UIThread.RunJobs();

        var articleSelected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bodySkeletonFading = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bodySkeletonHidden = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedNewsArticle) &&
                viewModel.SelectedNewsArticle is not null)
            {
                articleSelected.TrySetResult();
            }

            if (args.PropertyName == nameof(
                    MainWindowViewModel.IsNewsArticleBodySkeletonFadingOut) &&
                viewModel.IsNewsArticleBodySkeletonFadingOut)
            {
                bodySkeletonFading.TrySetResult();
            }

            if (args.PropertyName == nameof(
                    MainWindowViewModel.IsNewsArticleBodySkeletonVisible) &&
                !viewModel.IsNewsArticleBodySkeletonVisible)
            {
                bodySkeletonHidden.TrySetResult();
            }
        };
        articleCompletion.SetResult(new NewsArticleResponse
        {
            Title = "Uncached article",
            Url = "https://hytale.com/news/2026/8/uncached-article",
            PublishedAt = "2026-08-08",
            Author = "Hytale Team",
            Content = Enumerable.Range(0, 25)
                .Select(index => new NewsContentNode
                {
                    Kind = "paragraph",
                    Text = $"Loaded paragraph {index}."
                })
                .ToList()
        });
        await articleSelected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsNewsArticleBodyPreparing);
        Assert.False(viewModel.IsNewsArticleBodyVisible);
        var bodySkeleton = articleHost.GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(panel => panel.Name == "ArticleBodySkeleton");
        Assert.True(bodySkeleton.IsEffectivelyVisible);
        Assert.Equal(
            12,
            bodySkeleton.GetVisualDescendants()
                .OfType<Border>()
                .Count(border => border.Classes.Contains("contentTextSkeleton")));
        var bodySkeletonPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_ARTICLE_BODY_SKELETON_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(bodySkeletonPreviewPath))
        {
            var bodySkeletonFrame = window.CaptureRenderedFrame();
            Assert.NotNull(bodySkeletonFrame);
            bodySkeletonFrame!.Save(bodySkeletonPreviewPath, PngBitmapEncoderOptions.Default);
        }

        await bodySkeletonFading.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsNewsArticleBodySkeletonVisible);
        Assert.True(viewModel.IsNewsArticleBodySkeletonFadingOut);
        Assert.False(viewModel.IsNewsArticleBodyVisible);

        await bodySkeletonHidden.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsNewsArticleBodySkeletonVisible);
        Assert.False(viewModel.IsNewsArticleBodyVisible);

        await openTask;
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsNewsArticleBodyPreparing);
        Assert.True(viewModel.IsNewsArticleBodyVisible);
        Assert.False(bodySkeleton.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task NewsPaginationAndLanguageChangesApplyWithoutRestart()
    {
        using var cultureScope = new CultureRestoreScope();
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var language = "en-US";

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profile.Setup(service => service.GetNick()).Returns("Reader Test");
        settings.SetupGet(service => service.Language).Returns(() => language);
        settings.SetupSet(service => service.Language = It.IsAny<string>())
            .Callback<string>(value => language = value);
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>()))
            .ReturnsAsync((int count) => Enumerable.Range(1, count)
                .Select(index => new NewsItemResponse
                {
                    Title = $"News {index}",
                    Excerpt = "A paginated article excerpt.",
                    Url = $"https://hytale.com/news/2026/8/news-{index}",
                    Date = $"2026-08-{Math.Min(index, 28):00}",
                    Author = "Hytale Team"
                })
                .ToList());

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"));

        viewModel.NavigateCommand.Execute("news");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12, 1 + viewModel.LatestNews.Count);
        Assert.True(viewModel.CanShowLoadMore);

        await viewModel.LoadMoreNewsCommand.ExecuteAsync(null);
        Assert.Equal(20, 1 + viewModel.LatestNews.Count);
        news.Verify(service => service.GetNewsAsync(20), Times.Once);

        viewModel.NavigateCommand.Execute("settings");
        var settingsBeforeChange = viewModel.Settings;
        var window = new MainWindow
        {
            Width = 1100,
            Height = 760,
            DataContext = viewModel
        };
        window.Show();
        Assert.NotNull(window.CaptureRenderedFrame());

        var languageComboBox = window.GetVisualDescendants()
            .OfType<ComboBox>()
            .Single(comboBox => ReferenceEquals(comboBox.ItemsSource, viewModel.Settings.Languages));
        var fadingLanguageComboBox = Assert.IsType<FadingComboBox>(languageComboBox);
        Assert.All(viewModel.Settings.Languages, choice =>
        {
            Assert.True(choice.HasIcon);
            Assert.NotNull(choice.Icon);
            Assert.Equal(new PixelSize(72, 48), choice.Icon!.PixelSize);
        });
        Assert.Contains(
            languageComboBox.GetVisualDescendants().OfType<Image>(),
            image => ReferenceEquals(image.Source, viewModel.Settings.SelectedLanguage.Icon));

        fadingLanguageComboBox.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var languagePopup = fadingLanguageComboBox.GetVisualDescendants().OfType<FadingPopup>().Single();
        Assert.True(languagePopup.IsOpen);
        var languagePopupBorder = Assert.IsType<Border>(languagePopup.Child);
        Assert.Equal(8, Math.Abs(languagePopup.VerticalOffset));
        Assert.False(languagePopup.IsLightDismissEnabled);
        Assert.False(languagePopup.WindowManagerAddShadowHint);
        Assert.True(languagePopup.ShouldUseOverlayLayer);
        Assert.True(languagePopup.IsUsingOverlayLayer);
        Assert.Equal(new CornerRadius(18), languagePopupBorder.CornerRadius);
        Assert.Equal(
            Color.Parse("#1D1E21"),
            Assert.IsAssignableFrom<ISolidColorBrush>(languagePopupBorder.Background).Color);
        Assert.Equal(
            byte.MaxValue,
            Assert.IsAssignableFrom<ISolidColorBrush>(languagePopupBorder.Background).Color.A);
        var settingsContent = window.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(scrollViewer => scrollViewer.Name == "SettingsContent");
        var comboPositionBeforeScroll = fadingLanguageComboBox.TranslatePoint(default, window);
        var popupPositionBeforeScroll = languagePopupBorder.TranslatePoint(default, window);
        settingsContent.Offset = new Vector(0, 100);
        Dispatcher.UIThread.RunJobs();
        var comboPositionAfterScroll = fadingLanguageComboBox.TranslatePoint(default, window);
        var popupPositionAfterScroll = languagePopupBorder.TranslatePoint(default, window);
        Assert.NotNull(comboPositionBeforeScroll);
        Assert.NotNull(comboPositionAfterScroll);
        Assert.NotNull(popupPositionBeforeScroll);
        Assert.NotNull(popupPositionAfterScroll);
        var comboBottom = comboPositionBeforeScroll!.Value.Y + fadingLanguageComboBox.Bounds.Height;
        var popupBottom = popupPositionBeforeScroll!.Value.Y + languagePopupBorder.Bounds.Height;
        var popupGap = popupPositionBeforeScroll.Value.Y < comboPositionBeforeScroll.Value.Y
            ? comboPositionBeforeScroll.Value.Y - popupBottom
            : popupPositionBeforeScroll.Value.Y - comboBottom;
        Assert.Equal(8, popupGap, precision: 3);
        Assert.InRange(
            comboPositionBeforeScroll!.Value.Y - comboPositionAfterScroll!.Value.Y,
            99,
            101);
        Assert.InRange(
            popupPositionBeforeScroll!.Value.Y - popupPositionAfterScroll!.Value.Y,
            99,
            101);
        settingsContent.Offset = default;
        Dispatcher.UIThread.RunJobs();
        var settingsComboPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_SETTINGS_COMBO_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(settingsComboPreviewPath))
        {
            await WaitForConditionAsync(
                () => languagePopupBorder.Opacity >= 0.99,
                "language popup to finish opening");
            window.CaptureRenderedFrame()!.Save(settingsComboPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(settingsComboPreviewPath));
        }

        var languagePopupScrollBar = languagePopupBorder.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(scrollBar => scrollBar.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.True(fadingLanguageComboBox.IsDropDownInteractionSource(languagePopupScrollBar));
        var languageDropDownGlyph = fadingLanguageComboBox.GetVisualDescendants()
            .OfType<PathIcon>()
            .Single(icon => icon.Name == "DropDownGlyph");
        Assert.Equal(14, languageDropDownGlyph.Width);
        Assert.Equal(14, languageDropDownGlyph.Height);
        var german = viewModel.Settings.Languages.Single(choice => choice.Value == "de-DE");
        var germanContainer = languagePopupBorder.GetVisualDescendants()
            .OfType<ComboBoxItem>()
            .Single(item => ReferenceEquals(item.Content, german));
        Assert.Equal(new CornerRadius(9), germanContainer.CornerRadius);
        var germanPresenter = germanContainer.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Single(presenter => presenter.Name == "PART_ContentPresenter");
        Assert.Equal(new CornerRadius(9), germanPresenter.CornerRadius);
        Assert.Contains(
            germanPresenter.Transitions!,
            transition => transition is BrushTransition { Property: { } property } &&
                          property == TemplatedControl.BackgroundProperty);
        Assert.Contains(
            germanPresenter.Transitions!,
            transition => transition is BrushTransition { Property: { } property } &&
                          property == TemplatedControl.ForegroundProperty);
        var selectionEvent = new FocusChangedEventArgs(InputElement.GotFocusEvent)
        {
            Source = germanContainer
        };
        var languagePopupClosed = WaitForAvaloniaPropertyAsync(
            languagePopup,
            Popup.IsOpenProperty,
            () => !languagePopup.IsOpen,
            "language popup to finish closing");
        Assert.True(fadingLanguageComboBox.UpdateSelectionFromEvent(germanContainer, selectionEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(german, languageComboBox.SelectedItem);
        Assert.False(fadingLanguageComboBox.IsDropDownOpen);
        Assert.True(languagePopup.IsOpen);
        Assert.Contains(
            languagePopupBorder.Transitions!,
            transition => transition is DoubleTransition { Property: { } property } &&
                          property == Visual.OpacityProperty);
        Assert.NotNull(window.CaptureRenderedFrame());
        await languagePopupClosed;
        Dispatcher.UIThread.RunJobs();
        Assert.False(languagePopup.IsOpen);

        fadingLanguageComboBox.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(fadingLanguageComboBox.IsDropDownOpen);
        Assert.True(languagePopup.IsOpen);
        var languageDismissPoint = new Point(20, 20);
        var dismissedLanguagePopupClosed = WaitForAvaloniaPropertyAsync(
            languagePopup,
            Popup.IsOpenProperty,
            () => !languagePopup.IsOpen,
            "dismissed language popup to finish closing");
        window.MouseMove(languageDismissPoint);
        window.MouseDown(languageDismissPoint, MouseButton.Left);
        window.MouseUp(languageDismissPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(fadingLanguageComboBox.IsDropDownOpen);
        Assert.False(languagePopup.IsRequestedOpen);
        // The headless input dispatch pumps timers and may outlast the close
        // retention, so the popup may legitimately be closed or still fading
        // here; the selection phase above already covers the retention itself
        await dismissedLanguagePopupClosed;
        Dispatcher.UIThread.RunJobs();
        Assert.False(languagePopup.IsOpen);

        var russian = viewModel.Settings.Languages.Single(choice => choice.Value == "ru-RU");
        languageComboBox.SelectedItem = russian;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(settingsBeforeChange, viewModel.Settings);
        Assert.Same(russian, viewModel.Settings.SelectedLanguage);
        Assert.Equal("Новости", viewModel.NewsLabel);
        Assert.Equal("Настройки", viewModel.Settings.PageTitle);
        Assert.Equal("Офлайн-аккаунт", viewModel.AccountType);
        Assert.Equal("Загрузить ещё", viewModel.LoadMoreLabel);
        Assert.Equal(
            "Фон и отображение новостей",
            viewModel.Settings.Categories.Single(category => category.Id == "visual").Description);
        Assert.Equal(
            "Среда выполнения, путь к Java и аргументы JVM",
            viewModel.Settings.Categories.Single(category => category.Id == "java").Description);
        Assert.Equal(
            "О программе",
            viewModel.Settings.Categories.Single(category => category.Id == "about").Label);
        Assert.All(
            viewModel.Settings.Categories,
            category => Assert.False(category.Description.EndsWith('.')));
        settings.VerifySet(service => service.Language = "ru-RU", Times.Once);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SmoothNewsScrollerSupportsWheelAndMiddleClickAutoScroll()
    {
        var viewer = new SmoothScrollViewer
        {
            Width = 320,
            Height = 220,
            EnableMiddleClickAutoScroll = true,
            Content = new Border { Height = 1400 }
        };
        var window = new Window
        {
            Width = 420,
            Height = 320,
            Content = viewer
        };

        window.Show();
        Assert.NotNull(window.CaptureRenderedFrame());
        viewer.Measure(new Size(320, 220));
        viewer.Arrange(new Rect(0, 0, 320, 220));
        Dispatcher.UIThread.RunJobs();
        Assert.True(
            viewer.Extent.Height > viewer.Viewport.Height,
            $"Extent={viewer.Extent.Height}, viewport={viewer.Viewport.Height}");
        var center = viewer.TranslatePoint(new Point(160, 110), window);
        Assert.NotNull(center);

        window.MouseWheel(center!.Value, new Vector(0, -1), RawInputModifiers.None);
        Assert.Equal(0, viewer.Offset.Y);
        await WaitForConditionAsync(
            () => viewer.Offset.Y > 0,
            "smooth scrolling after a wheel event");
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.Offset.Y > 0);
        var afterMouseWheel = viewer.Offset.Y;

        window.MouseWheel(center.Value, new Vector(0, -0.25), RawInputModifiers.None);
        await WaitForConditionAsync(
            () => viewer.Offset.Y > afterMouseWheel,
            "smooth scrolling after a precision touchpad delta");
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.Offset.Y > afterMouseWheel);

        window.MouseDown(center.Value, MouseButton.Middle);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.SizeAll).ToString(), viewer.Cursor?.ToString());
        var beforeAutoScroll = viewer.Offset.Y;
        window.MouseMove(center.Value + new Vector(0, 80));
        await WaitForConditionAsync(
            () => viewer.Offset.Y > beforeAutoScroll,
            "middle-click auto-scroll movement");
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.Offset.Y > beforeAutoScroll);
        Assert.Equal(
            new Cursor(StandardCursorType.BottomSide).ToString(),
            viewer.Cursor?.ToString());

        window.MouseMove(center.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.SizeAll).ToString(), viewer.Cursor?.ToString());

        window.MouseMove(center.Value - new Vector(0, 80));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.TopSide).ToString(), viewer.Cursor?.ToString());

        window.MouseDown(center.Value + new Vector(0, 80), MouseButton.Middle);
        window.MouseUp(center.Value + new Vector(0, 80), MouseButton.Middle);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1024, 700, false)]
    [InlineData(1280, 800, true)]
    [InlineData(1920, 900, true)]
    public async Task ShellRendersAtSupportedDesktopSizes(int width, int height, bool isOfficialProfile)
    {
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var github = new Mock<IGitHubClient>();

        var selected = new InstanceInfo
        {
            Id = "preview",
            Name = "Hytale Release",
            Branch = "release",
            Version = 42,
            IsInstalled = true
        };

        instances.Setup(service => service.GetCachedInstances())
            .Returns([selected]);
        instances.Setup(service => service.GetSelectedInstance())
            .Returns(selected);
        instances.Setup(service => service.GetInstancePathById(selected.Id))
            .Returns("/tmp/hyprism-preview-instance");
        instances.Setup(service => service.IsClientPresent(It.IsAny<string>()))
            .Returns(true);
        profile.Setup(service => service.GetNick()).Returns("Hyprism Player");
        profile.Setup(service => service.GetAvatarPreviewForUUID(It.IsAny<string>()))
            .Returns($"data:image/png;base64,{Convert.ToBase64String(TinyPngHandler.ImageBytes)}");
        profileManagement.Setup(service => service.GetSelectedProfile())
            .Returns(new Profile
            {
                Id = "active-profile",
                Name = "Hyprism Player",
                UUID = Guid.NewGuid().ToString(),
                IsOfficial = isOfficialProfile
            });
        profileManagement.Setup(service => service.GetProfiles())
            .Returns(() =>
            [
                new Profile
                {
                    Id = "active-profile",
                    Name = "Hyprism Player",
                    UUID = "00000000-0000-0000-0000-000000000001",
                    IsOfficial = isOfficialProfile
                }
            ]);
        profileManagement.Setup(service => service.GetSelectedProfileId())
            .Returns("active-profile");
        github.Setup(service => service.GetContributorsAsync()).ReturnsAsync(
        [
            new GitHubUser { Login = "yyyumeniku", Type = "User" },
            new GitHubUser { Login = "Aarav2709", Type = "User" },
            .. Enumerable.Range(1, 10).Select(index => new GitHubUser
            {
                Login = $"contributor-{index}",
                AvatarUrl = $"https://avatars.githubusercontent.com/u/{index}",
                HtmlUrl = $"https://github.com/contributor-{index}",
                Type = "User",
                Contributions = 20 - index
            }),
            new GitHubUser { Login = "dependabot[bot]", Type = "Bot" },
            new GitHubUser { Login = "release-bot", Type = "User" }
        ]);
        github.Setup(service => service.GetLatestMainCommitAsync()).ReturnsAsync(
            new GitHubCommit(
                "abcdef1234567890",
                "feat: refine the native About page",
                "https://github.com/hyprismteam/Hyprism/commit/abcdef1"));
        github.Setup(service => service.LoadAvatarAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(TinyPngHandler.ImageBytes);
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new NewsItemResponse
                {
                    Title = "PRE-RELEASE PATCH NOTES (UPDATE 7)",
                    Excerpt = "A closer look at the world, its creatures, and the systems behind exploration.",
                    Url = "https://hytale.com/news/preview",
                    Date = "2026-08-05",
                    Author = "Hytale Team"
                },
                new NewsItemResponse
                {
                    Title = "Inside Hytale's latest world update",
                    Excerpt = "New environments and discoveries await.",
                    Url = "https://hytale.com/news/world-update",
                    Date = "2026-08-03",
                    Author = "Hytale Team"
                },
                new NewsItemResponse
                {
                    Title = "Building a world worth exploring",
                    Excerpt = "Meet the artists shaping Hytale's regions.",
                    Url = "https://hytale.com/news/world-art",
                    Date = "2026-08-02",
                    Author = "Hytale Team"
                },
                new NewsItemResponse
                {
                    Title = "A new look at adventure mode",
                    Excerpt = "The team shares its latest design work.",
                    Url = "https://hytale.com/news/adventure-mode",
                    Date = "2026-08-01",
                    Author = "Hytale Team"
                }
            ]);
        news.Setup(service => service.GetNewsArticleAsync(It.IsAny<string>()))
            .ReturnsAsync(new NewsArticleResponse
            {
                Title = "PRE-RELEASE PATCH NOTES (UPDATE 7)",
                Excerpt = "A closer look at the world and its creatures.",
                Url = "https://hytale.com/news/2026/8/new-adventure",
                PublishedAt = "2026-08-05",
                Author = "Hytale Team",
                Categories = ["Game Update", "World Design"],
                Content =
                [
                    new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode { Kind = "text", Text = "Explore a " },
                            new NewsContentNode
                            {
                                Kind = "bold",
                                Children = [new NewsContentNode { Kind = "text", Text = "living world" }]
                            },
                            new NewsContentNode
                            {
                                Kind = "text",
                                Text = " from inside the launcher. Documentation: "
                            },
                            new NewsContentNode
                            {
                                Kind = "link",
                                Url = "https://hytale.com/news",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "Hytale News"
                                    }
                                ]
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "heading",
                        Level = 2,
                        Children = [new NewsContentNode { Kind = "text", Text = "Creative Gameplay Quality" }]
                    },
                    new NewsContentNode
                    {
                        Kind = "blockquote",
                        Children = [new NewsContentNode { Kind = "text", Text = "Every world tells a story." }]
                    },
                    new NewsContentNode
                    {
                        Kind = "unordered-list",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "list-item",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "bold",
                                        Children =
                                        [
                                            new NewsContentNode
                                            {
                                                Kind = "text",
                                                Text = "Customize items further"
                                            }
                                        ]
                                    },
                                    new NewsContentNode
                                    {
                                        Kind = "unordered-list",
                                        Children =
                                        [
                                            new NewsContentNode
                                            {
                                                Kind = "list-item",
                                                Children =
                                                [
                                                    new NewsContentNode
                                                    {
                                                        Kind = "text",
                                                        Text = "Website and documentation: "
                                                    },
                                                    new NewsContentNode
                                                    {
                                                        Kind = "link",
                                                        Url = "https://hytalemodding.dev/",
                                                        Children =
                                                        [
                                                            new NewsContentNode
                                                            {
                                                                Kind = "text",
                                                                Text = "https://hytalemodding.dev/"
                                                            }
                                                        ]
                                                    },
                                                    new NewsContentNode
                                                    {
                                                        Kind = "text",
                                                        Text = ". Policies: "
                                                    },
                                                    new NewsContentNode
                                                    {
                                                        Kind = "link",
                                                        Url = "https://hytale.com/server-policies",
                                                        Children =
                                                        [
                                                            new NewsContentNode
                                                            {
                                                                Kind = "text",
                                                                Text = "Server Owner Policies"
                                                            }
                                                        ]
                                                    }
                                                ]
                                            },
                                            new NewsContentNode
                                            {
                                                Kind = "list-item",
                                                Children =
                                                [
                                                    new NewsContentNode
                                                    {
                                                        Kind = "text",
                                                        Text = "New creatures"
                                                    }
                                                ]
                                            }
                                        ]
                                    }
                                ]
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "details",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "summary",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "The technical details "
                                    },
                                    new NewsContentNode
                                    {
                                        Kind = "inline-image",
                                        ImageUrl = "https://cdn.hytale.com/emotes/hypixel-this-is-fine.png",
                                        ImagePresentation = "emote",
                                        AltText = ":hypixel-this-is-fine:"
                                    }
                                ]
                            },
                            new NewsContentNode
                            {
                                Kind = "paragraph",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "Hidden implementation notes."
                                    }
                                ]
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode { Kind = "text", Text = "Run " },
                            new NewsContentNode { Kind = "inline-code", Text = "worldgen.reload()" },
                            new NewsContentNode { Kind = "text", Text = " to rebuild the preview." }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "code-block",
                        Text = "worldgen.reload();\nserver.save();"
                    }
                ]
            });

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            null,
            github.Object);

        Assert.Equal(
            isOfficialProfile ? "Hytale Account" : "Offline Account",
            viewModel.AccountType);

        var window = new MainWindow
        {
            Width = width,
            Height = height,
            DataContext = viewModel
        };

        window.Show();
        var frame = window.CaptureRenderedFrame();
        var handCursor = new Cursor(StandardCursorType.Hand).ToString();
        var arrowCursor = new Cursor(StandardCursorType.Arrow).ToString();
        var sidebarProfileAvatarHost = window.FindControl<Border>("SidebarProfileAvatarHost");
        var sidebarProfileAvatar = window.FindControl<Image>("SidebarProfileAvatar");

        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(width, height), frame!.PixelSize);
        Assert.True(viewModel.HasActiveProfileAvatar);
        Assert.NotNull(sidebarProfileAvatarHost);
        Assert.True(sidebarProfileAvatarHost!.ClipToBounds);
        Assert.Equal(new CornerRadius(22), sidebarProfileAvatarHost.CornerRadius);
        Assert.NotNull(sidebarProfileAvatar);
        Assert.True(sidebarProfileAvatar!.IsEffectivelyVisible);
        Assert.Same(viewModel.ActiveProfileAvatar, sidebarProfileAvatar.Source);
        Assert.All(
            window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEnabled && !button.Classes.Contains("window")),
            button => Assert.Equal(handCursor, button.Cursor?.ToString()));
        Assert.All(
            new[]
            {
                window.FindControl<Button>("MinimizeWindowButton"),
                window.FindControl<Button>("MaximizeWindowButton"),
                window.FindControl<Button>("CloseWindowButton")
            },
            button => Assert.Equal(arrowCursor, button!.Cursor?.ToString()));
        Assert.All(
            window.GetVisualDescendants().OfType<ComboBox>().Where(comboBox => comboBox.IsEnabled),
            comboBox => Assert.Equal(handCursor, comboBox.Cursor?.ToString()));

        var mainSceneFrame = window.FindControl<Border>("MainSceneFrame");
        var mainSceneSurface = window.FindControl<Border>("MainSceneSurface");
        Assert.NotNull(mainSceneFrame);
        Assert.False(mainSceneFrame!.ClipToBounds);
        Assert.Equal(new CornerRadius(24), mainSceneFrame.CornerRadius);
        Assert.Equal(new Thickness(1), mainSceneFrame.BorderThickness);
        Assert.NotNull(mainSceneSurface);
        Assert.True(mainSceneSurface!.ClipToBounds);
        Assert.Equal(new CornerRadius(23), mainSceneSurface.CornerRadius);

        var shellInstancesView = Assert.Single(
            window.GetVisualDescendants().OfType<InstancesView>());
        Assert.True(shellInstancesView.IsEffectivelyVisible);
        Assert.NotNull(shellInstancesView.FindControl<InstanceListView>("InstanceListContentView")?
            .FindControl<Border>("InstancesListPane"));
        Assert.NotNull(shellInstancesView.FindControl<Grid>("InstancesContent"));

        Assert.NotNull(window.FindControl<Border>("ResizeNorth")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeSouth")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeWest")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeEast")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeNorthWest")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeNorthEast")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeSouthWest")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeSouthEast")?.Cursor);

        var primaryNavigation = window.FindControl<StackPanel>("PrimaryNavigation");
        Assert.NotNull(primaryNavigation);
        Assert.DoesNotContain(
            primaryNavigation!.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Mods");

        var minimizeButton = window.FindControl<Button>("MinimizeWindowButton");
        var maximizeButton = window.FindControl<Button>("MaximizeWindowButton");
        var closeButton = window.FindControl<Button>("CloseWindowButton");
        Assert.NotNull(minimizeButton);
        Assert.NotNull(maximizeButton);
        Assert.NotNull(closeButton);
        Assert.All(
            new[] { minimizeButton!, maximizeButton!, closeButton! },
            button => Assert.Contains(
                button.Transitions!,
                transition => transition is BrushTransition));

        var minimizeGlyph = window.FindControl<Avalonia.Controls.Shapes.Path>("MinimizeWindowIcon");
        Assert.NotNull(minimizeGlyph);
        var minimizePosition = minimizeGlyph!.TranslatePoint(default, minimizeButton);
        Assert.NotNull(minimizePosition);
        Assert.True(minimizePosition!.Value.Y > minimizeButton.Bounds.Height / 2);
        Assert.Contains(
            minimizeGlyph.Transitions!,
            transition => transition is BrushTransition);

        var maximizeGlyph = window.FindControl<Avalonia.Controls.Shapes.Path>("MaximizeWindowIcon");
        var restoreGlyph = window.FindControl<Avalonia.Controls.Shapes.Path>("RestoreWindowIcon");
        var closeGlyph = Assert.Single(
            closeButton!.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
        Assert.NotNull(maximizeGlyph);
        Assert.NotNull(restoreGlyph);
        Assert.True(maximizeGlyph!.IsVisible);
        Assert.False(restoreGlyph!.IsVisible);
        Assert.All(
            new[] { maximizeGlyph, restoreGlyph, closeGlyph },
            icon => Assert.Contains(
                icon!.Transitions!,
                transition => transition is BrushTransition));

        if (width == 1280)
        {
            var instancesButton = window.FindControl<Button>("InstancesNavButton");
            Assert.NotNull(instancesButton);

            var button = instancesButton!;
            var originalBounds = button.Bounds;
            var hoverPoint = button.TranslatePoint(
                new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                window);
            Assert.NotNull(hoverPoint);

            window.MouseMove(hoverPoint!.Value);
            Dispatcher.UIThread.RunJobs();

            Assert.True(button.IsPointerOver);
            Assert.Equal(originalBounds, button.Bounds);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color.A);
            Assert.DoesNotContain(
                button.GetVisualDescendants(),
                visual => visual.RenderTransform is ScaleTransform);
            AssertNoPressScale(button);

            window.MouseDown(hoverPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(originalBounds, button.Bounds);
            Assert.DoesNotContain(
                button.GetVisualDescendants(),
                visual => visual.RenderTransform is ScaleTransform);
            AssertNoPressScale(button);

            window.MouseUp(hoverPoint.Value, MouseButton.Left);
        }

        viewModel.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();
        var instancesView = Assert.Single(window.GetVisualDescendants().OfType<InstancesView>());
        var usesCompactInstancesLayout = instancesView.Bounds.Width < 940;
        Assert.True(instancesView.IsEffectivelyVisible);
        Assert.DoesNotContain(
            instancesView.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Instances");
        var instanceListGroup = Assert.Single(
            instancesView.GetVisualDescendants().OfType<StackPanel>(),
            panel => panel.Classes.Contains("managerRailList"));
        var instancesScroll = Assert.Single(
            instancesView.GetVisualDescendants().OfType<ScrollViewer>(),
            scroll => scroll.Classes.Contains("managerRailScroll"));
        Assert.Equal(new Thickness(14, 18, 4, 18), instancesScroll.Margin);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, instancesScroll.HorizontalContentAlignment);
        Assert.Equal(new Thickness(0, 0, 10, 0), instanceListGroup.Margin);
        Assert.Equal(2, instanceListGroup.Spacing);
        Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("managerAddRow"));
        Assert.Equal(
            2,
            instanceListGroup.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Count(path => path.Classes.Contains("managerListHandle") || path.Classes.Contains("managerListMore")));
        var instanceDragHandle = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("managerListHandle"));
        Assert.Equal(14, instanceDragHandle.Bounds.Width);
        Assert.Equal(arrowCursor, instanceDragHandle.Cursor?.ToString());
        var instanceDragTarget = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("managerListDragTarget"));
        Assert.Equal(usesCompactInstancesLayout ? 44 : 40, instanceDragTarget.Bounds.Width);
        Assert.Equal(usesCompactInstancesLayout ? 44 : 40, instanceDragTarget.Bounds.Height);
        Assert.Equal(arrowCursor, instanceDragTarget.Cursor?.ToString());
        var managedInstanceRow = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("managerListItem") && button.Classes.Contains("managed"));
        Assert.Equal(handCursor, managedInstanceRow.Cursor?.ToString());
        Assert.Equal(usesCompactInstancesLayout ? 78 : 72, managedInstanceRow.Bounds.Height);
        Assert.Equal(new CornerRadius(11), managedInstanceRow.CornerRadius);
        if (!usesCompactInstancesLayout)
        {
            await WaitForConditionAsync(
                () => managedInstanceRow.Background is ISolidColorBrush { Color: var color } &&
                      color == Color.Parse("#12FFFFFF"),
                "selected instance row background");
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(
            usesCompactInstancesLayout ? Colors.Transparent : Color.Parse("#12FFFFFF"),
            Assert.IsAssignableFrom<ISolidColorBrush>(managedInstanceRow.Background).Color);
        var managedInstanceTitle = Assert.Single(
            managedInstanceRow.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Classes.Contains("managerListTitle"));
        var managedInstanceDescription = Assert.Single(
            managedInstanceRow.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Classes.Contains("managerCategoryDescription"));
        Assert.Equal(15, managedInstanceTitle.FontSize);
        Assert.Equal(11, managedInstanceDescription.FontSize);
        var managedInstanceGameIcon = Assert.Single(
            managedInstanceRow.GetVisualDescendants().OfType<Grid>(),
            icon => icon.Classes.Contains("instancesListGameIcon"));
        Assert.NotNull(Assert.Single(managedInstanceGameIcon.Children.OfType<Image>(), image => image.IsVisible).Source);
        Assert.Equal(usesCompactInstancesLayout, managedInstanceGameIcon.IsVisible);
        Assert.Equal(usesCompactInstancesLayout ? 38 : 0, managedInstanceGameIcon.Width);
        Assert.Equal(usesCompactInstancesLayout ? 38 : 0, managedInstanceGameIcon.Height);
        var managedInstanceRowPoint = managedInstanceRow.TranslatePoint(
            new Point(managedInstanceRow.Bounds.Width / 2, managedInstanceRow.Bounds.Height / 2),
            window);
        Assert.NotNull(managedInstanceRowPoint);
        window.MouseMove(managedInstanceRowPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(managedInstanceRow.IsPointerOver);
        var expectedManagedInstanceHoverAlpha = usesCompactInstancesLayout ? (byte)8 : (byte)24;
        await WaitForConditionAsync(
            () => managedInstanceRow.Background is ISolidColorBrush { Color.A: var alpha } &&
                  alpha >= expectedManagedInstanceHoverAlpha - 1,
            "managed instance row hover background");
        Dispatcher.UIThread.RunJobs();
        var managedInstanceHoverColor =
            Assert.IsAssignableFrom<ISolidColorBrush>(managedInstanceRow.Background).Color;
        Assert.Equal(byte.MaxValue, managedInstanceHoverColor.R);
        Assert.Equal(byte.MaxValue, managedInstanceHoverColor.G);
        Assert.Equal(byte.MaxValue, managedInstanceHoverColor.B);
        Assert.InRange(
            managedInstanceHoverColor.A,
            (byte)(expectedManagedInstanceHoverAlpha - 1),
            expectedManagedInstanceHoverAlpha);

        var inactiveInstance = new InstanceItemViewModel(
            "inactive-preview",
            "Inactive Preview",
            "v41",
            "Pre-Release",
            false,
            false);
        viewModel.AllInstances.Add(inactiveInstance);
        Dispatcher.UIThread.RunJobs();
        var inactiveInstanceRow = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("managerListItem") && !button.Classes.Contains("managed"));
        Assert.Equal(
            Colors.Transparent,
            Assert.IsAssignableFrom<ISolidColorBrush>(inactiveInstanceRow.Background).Color);
        var inactiveInstanceRowPoint = inactiveInstanceRow.TranslatePoint(
            new Point(inactiveInstanceRow.Bounds.Width / 2, inactiveInstanceRow.Bounds.Height / 2),
            window);
        Assert.NotNull(inactiveInstanceRowPoint);
        window.MouseMove(inactiveInstanceRowPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(inactiveInstanceRow.IsPointerOver);
        await WaitForConditionAsync(
            () => inactiveInstanceRow.Background is ISolidColorBrush { Color.A: >= 7 },
            "inactive instance row hover background");
        Dispatcher.UIThread.RunJobs();
        var inactiveInstanceHoverColor =
            Assert.IsAssignableFrom<ISolidColorBrush>(inactiveInstanceRow.Background).Color;
        Assert.Equal(byte.MaxValue, inactiveInstanceHoverColor.R);
        Assert.Equal(byte.MaxValue, inactiveInstanceHoverColor.G);
        Assert.Equal(byte.MaxValue, inactiveInstanceHoverColor.B);
        Assert.InRange(inactiveInstanceHoverColor.A, (byte)7, (byte)8);
        window.MouseMove(new Point(0, 0));
        Dispatcher.UIThread.RunJobs();
        viewModel.AllInstances.Remove(inactiveInstance);
        Dispatcher.UIThread.RunJobs();

        var addInstanceRow = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("managerAddRow"));
        Assert.Equal(default, addInstanceRow.CornerRadius);
        Assert.Equal(default, addInstanceRow.BorderThickness);
        Assert.Contains(
            addInstanceRow.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == viewModel.NewInstanceTitle);
        Assert.Equal(
            Colors.Transparent,
            Assert.IsAssignableFrom<ISolidColorBrush>(addInstanceRow.Background).Color);
        Assert.Equal(
            14,
            Assert.Single(addInstanceRow.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()).Bounds.Width);

        var dragPoint = instanceDragTarget.TranslatePoint(
            new Point(instanceDragTarget.Bounds.Width - 3, instanceDragTarget.Bounds.Height / 2),
            window);
        Assert.NotNull(dragPoint);
        var resolvedDragPoint = dragPoint!.Value;
        var dragPreview = instancesView.FindControl<Border>("InstanceDragPreview");
        Assert.NotNull(dragPreview);
        window.MouseMove(resolvedDragPoint);
        window.MouseDown(resolvedDragPoint, MouseButton.Left);
        window.MouseMove(resolvedDragPoint + new Vector(36, 24));
        Dispatcher.UIThread.RunJobs();
        Assert.True(dragPreview!.IsVisible);
        Assert.Equal(default, dragPreview.BorderThickness);
        Assert.Equal(new CornerRadius(11), dragPreview.CornerRadius);
        var dragPreviewGameIcon = Assert.Single(
            dragPreview.GetVisualDescendants().OfType<Image>(),
            image => image.Classes.Contains("instancesListGameIcon"));
        Assert.Equal(usesCompactInstancesLayout, dragPreviewGameIcon.IsVisible);
        Assert.Equal(usesCompactInstancesLayout ? 38 : 0, dragPreviewGameIcon.Width);
        var dragPreviewPath = Environment.GetEnvironmentVariable(
            usesCompactInstancesLayout
                ? "HYPRISM_INSTANCES_COMPACT_DRAG_RENDER_OUTPUT"
                : "HYPRISM_INSTANCES_DRAG_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(dragPreviewPath))
            window.CaptureRenderedFrame()!.Save(dragPreviewPath, PngBitmapEncoderOptions.Default);
        window.MouseUp(resolvedDragPoint + new Vector(36, 24), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(dragPreview.IsVisible);
        var managerActions = instancesView.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("managerAction"))
            .ToList();
        Assert.Equal(4, managerActions.Count);
        Assert.Single(managerActions, button => button.Classes.Contains("primary"));
        Assert.Single(managerActions, button => button.Classes.Contains("editAction"));
        Assert.Single(managerActions, button => button.Classes.Contains("danger"));
        var instanceMenuRows = instancesView.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("instanceMenuRow"))
            .ToList();
        Assert.Equal(4, instanceMenuRows.Count);
        var instanceMenuIcons = instancesView.GetVisualDescendants()
            .OfType<Image>()
            .Where(image => image.Classes.Contains("instanceMenuIcon"))
            .ToList();
        Assert.Equal(4, instanceMenuIcons.Count);
        Assert.All(instanceMenuIcons, icon =>
        {
            Assert.Equal(28, icon.Width);
            Assert.Equal(28, icon.Height);
            Assert.NotNull(icon.Source);
        });
        Assert.All(instanceMenuRows.Take(3), row => Assert.InRange(row.Bounds.Height, 68.5, 69.5));
        Assert.InRange(instanceMenuRows[^1].Bounds.Height, 65.5, 66.5);
        Assert.DoesNotContain(
            instancesView.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("instanceLaunch"));
        var instanceGameIcon = Assert.Single(
            instancesView.GetVisualDescendants().OfType<Grid>(),
            icon => icon.Classes.Contains("instanceGameIcon"));
        Assert.Equal(144, instanceGameIcon.Width);
        Assert.Equal(144, instanceGameIcon.Height);
        Assert.NotNull(Assert.Single(instanceGameIcon.Children.OfType<Image>(), image => image.IsVisible).Source);
        Assert.Equal(instancesView.Bounds.Width >= 940, instanceGameIcon.IsVisible);
        var compactInstanceGameIcon = Assert.Single(
            instancesView.GetVisualDescendants().OfType<Grid>(),
            icon => icon.Classes.Contains("compactInstanceGameIcon"));
        Assert.Equal(30, compactInstanceGameIcon.Width);
        Assert.Equal(30, compactInstanceGameIcon.Height);
        Assert.NotNull(Assert.Single(compactInstanceGameIcon.Children.OfType<Image>(), image => image.IsVisible).Source);
        var instanceSummary = Assert.Single(
            instancesView.GetVisualDescendants().OfType<StackPanel>(),
            panel => panel.Classes.Contains("instanceSummary"));
        var instanceOverview = instancesView.FindControl<InstanceOverviewView>("InstanceOverviewContentView");
        var instanceHubContent = instanceOverview?.FindControl<StackPanel>("InstanceHubContent");
        Assert.NotNull(instanceHubContent);
        Assert.Equal(instanceHubContent!.Spacing, instanceSummary.Spacing);

        var instancesContent = instancesView.FindControl<Grid>("InstancesContent");
        var instancesListPane = instancesView.FindControl<InstanceListView>("InstanceListContentView")?
            .FindControl<Border>("InstancesListPane");
        var compactInstanceToolbar = instanceOverview?.FindControl<Border>("CompactInstanceToolbar");
        var managerCompactSplitAction = instancesView.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("managerCompactSplitAction") &&
                              border.GetVisualDescendants().OfType<Button>()
                                  .Any(button => button.Name == "CompactInstancePrimaryAction"));
        var compactInstancePrimaryAction = instanceOverview?.FindControl<Button>("CompactInstancePrimaryAction");
        var compactInstanceMoreButton = instanceOverview?.FindControl<Button>("CompactInstanceMoreButton");
        var compactInstanceMenuPopup = instanceOverview?.FindControl<FadingPopup>("CompactInstanceMenuPopup");
        var managerWideActions = instanceOverview?.FindControl<StackPanel>("WideInstanceActions");
        Assert.NotNull(instancesContent);
        Assert.NotNull(instancesListPane);
        if (!usesCompactInstancesLayout)
            Assert.Equal(276, instancesListPane!.Bounds.Width);
        Assert.NotNull(compactInstanceToolbar);
        Assert.NotNull(compactInstancePrimaryAction);
        Assert.NotNull(compactInstanceMoreButton);
        Assert.NotNull(compactInstanceMenuPopup);
        Assert.False(compactInstanceMenuPopup!.WindowManagerAddShadowHint);
        Assert.False(compactInstanceMenuPopup.IsLightDismissEnabled);
        Assert.NotNull(managerWideActions);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, managerWideActions!.HorizontalAlignment);
        var instanceContentTranslation = Assert.IsType<TranslateTransform>(instancesContent!.RenderTransform);
        Assert.Equal(usesCompactInstancesLayout, compactInstanceToolbar!.IsVisible);
        var creatorView = Assert.IsType<InstanceCreatorView>(
            instancesView.FindControl<InstanceCreatorView>("InstanceCreatorContentView"));
        var instanceWizardReveal = creatorView.FindControl<WizardRevealIcon>("InstanceWizardReveal");
        Assert.NotNull(instanceWizardReveal);
        var instanceWizardAnimation = instanceWizardReveal.Animation;
        Assert.Equal("/Assets/Lotties/server-reveal.json", instanceWizardAnimation.Path);
        Assert.True(instanceWizardAnimation.AutoPlay);
        Assert.Equal(2, instanceWizardAnimation.PlayBackRate);
        Assert.Equal(1, instanceWizardAnimation.RepeatCount);
        Assert.NotNull(instanceWizardAnimation.OpacityMask);
        Assert.Equal(64, instanceWizardAnimation.Width);
        Assert.Equal(64, instanceWizardAnimation.Height);

        if (!usesCompactInstancesLayout)
        {
            var contentWidthWithList = instancesContent.Bounds.Width;
            addInstanceRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForConditionAsync(
                () => instancesListPane!.Bounds.Width <= 0.5 &&
                      instancesListPane.Opacity <= 0.01 &&
                      instanceWizardAnimation.IsEffectivelyVisible,
                "wide instance creator to open");
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(instancesListPane!.Bounds.Width, 0, 0.5);
            Assert.InRange(instancesListPane.Opacity, 0, 0.01);
            Assert.False(instancesListPane.IsHitTestVisible);
            Assert.True(instancesContent.Bounds.Width > contentWidthWithList + 275);
            Assert.True(instanceWizardAnimation.IsEffectivelyVisible);

            var instanceWizardPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_INSTANCE_WIZARD_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(instanceWizardPreviewPath) && width == 1280)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(
                    instanceWizardPreviewPath,
                    PngBitmapEncoderOptions.Default);
            }

            var instancesListWidthRestored = WaitForAvaloniaPropertyAsync(
                instancesListPane,
                Visual.BoundsProperty,
                () => Math.Abs(instancesListPane.Bounds.Width - 276) < 0.5,
                "instances list width to be restored");
            viewModel.CloseInstanceCreatorCommand.Execute(null);
            await instancesListWidthRestored;
            await AvaloniaTestWait.UntilAsync(
                () => !instancesListPane.IsAnimating(Visual.OpacityProperty),
                "instances list opacity to be restored");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(276, instancesListPane.Bounds.Width);
            Assert.Equal(1, instancesListPane.Opacity);
            Assert.True(instancesListPane.IsHitTestVisible);
        }

        if (usesCompactInstancesLayout)
        {
            Assert.True(instanceContentTranslation.X > 0);
            var compactListPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_INSTANCES_COMPACT_LIST_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactListPreviewPath))
            {
                var compactListFrame = window.CaptureRenderedFrame();
                Assert.NotNull(compactListFrame);
                compactListFrame!.Save(compactListPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactListPreviewPath));
            }

            var instanceCreatorOpened = WaitForAvaloniaPropertyAsync(
                instanceContentTranslation,
                TranslateTransform.XProperty,
                () => instanceContentTranslation.X == 0,
                "compact instance creator to finish opening");
            addInstanceRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var instanceCreatorScreen = instancesView.FindControl<Border>("InstanceCreatorScreen");
            var instancesOverview = instancesView.FindControl<Grid>("InstancesOverview");
            Assert.True(instanceCreatorScreen!.IsVisible);
            Assert.False(instancesOverview!.IsVisible);
            await instanceCreatorOpened;
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsInstanceCreatorOpen);
            Assert.Equal(0, instanceContentTranslation.X);
            Assert.True(instanceCreatorScreen!.IsEffectivelyVisible);
            Assert.True(instanceWizardAnimation.IsEffectivelyVisible);
            Assert.False(instancesOverview!.IsVisible);

            var instanceCreatorClosed = WaitForAvaloniaPropertyAsync(
                instanceCreatorScreen,
                Visual.IsVisibleProperty,
                () => !instanceCreatorScreen.IsVisible,
                "compact instance creator to finish closing");
            viewModel.CloseInstanceCreatorCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(instanceCreatorScreen.IsVisible);
            await instanceCreatorClosed;
            Dispatcher.UIThread.RunJobs();
            Assert.True(instanceContentTranslation.X > 0);
            Assert.False(instanceCreatorScreen.IsVisible);
            Assert.True(instancesOverview.IsVisible);

            var instanceButton = instancesListPane!.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("managerListItem"));
            var instanceContentOpened = WaitForAvaloniaPropertyAsync(
                instanceContentTranslation,
                TranslateTransform.XProperty,
                () => instanceContentTranslation.X == 0,
                "compact instance content to finish opening");
            instanceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await instanceContentOpened;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, instanceContentTranslation.X);
            Assert.False(managerWideActions.IsVisible);
            Assert.True(managerCompactSplitAction.IsEffectivelyVisible);
            Assert.True(compactInstancePrimaryAction!.IsEffectivelyVisible);
            Assert.True(compactInstanceGameIcon.IsEffectivelyVisible);

            var compactActionRight = managerCompactSplitAction.TranslatePoint(
                new Point(managerCompactSplitAction.Bounds.Width, 0),
                window);
            var compactContentRight = instanceHubContent!.TranslatePoint(
                new Point(instanceHubContent.Bounds.Width, 0),
                window);
            Assert.NotNull(compactActionRight);
            Assert.NotNull(compactContentRight);
            Assert.InRange(
                Math.Abs(compactActionRight!.Value.X - compactContentRight!.Value.X),
                0,
                0.5);

            var compactMenuPoint = compactInstanceMoreButton!.TranslatePoint(
                new Point(compactInstanceMoreButton.Bounds.Width / 2, compactInstanceMoreButton.Bounds.Height / 2),
                window);
            Assert.NotNull(compactMenuPoint);
            window.MouseMove(compactMenuPoint!.Value);
            window.MouseDown(compactMenuPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceMenuPopup!.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);
            await WaitForConditionAsync(
                () => compactInstanceMenuPopup.Child?.Opacity >= 0.99,
                "compact instance menu to finish opening");
            Dispatcher.UIThread.RunJobs();

            var compactMenuPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_INSTANCES_COMPACT_MENU_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactMenuPreviewPath))
            {
                window.CaptureRenderedFrame()!.Save(compactMenuPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactMenuPreviewPath));
            }

            window.MouseMove(compactMenuPoint.Value);
            window.MouseDown(compactMenuPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);

            var compactMenuClosingPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_INSTANCES_COMPACT_MENU_CLOSING_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactMenuClosingPreviewPath))
            {
                await WaitForConditionAsync(
                    () => compactInstanceMenuPopup.Child?.IsAnimating(Visual.OpacityProperty) == true,
                    "compact instance menu close animation to start");
                window.CaptureRenderedFrame()!.Save(compactMenuClosingPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactMenuClosingPreviewPath));
            }

            await WaitForConditionAsync(
                () => !compactInstanceMenuPopup.IsOpen,
                "compact instance menu to close");
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsOpen);

            window.MouseMove(compactMenuPoint.Value);
            window.MouseDown(compactMenuPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceMenuPopup.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);
            await WaitForConditionAsync(
                () => compactInstanceMenuPopup.Child?.Opacity >= 0.99,
                "compact instance menu to finish reopening");
            Dispatcher.UIThread.RunJobs();

            var compactMenuDismissPoint = instanceHubContent!.TranslatePoint(new Point(40, 110), window);
            Assert.NotNull(compactMenuDismissPoint);
            window.MouseMove(compactMenuDismissPoint!.Value);
            window.MouseDown(compactMenuDismissPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuDismissPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);
            await WaitForConditionAsync(
                () => !compactInstanceMenuPopup.IsOpen,
                "compact instance menu light dismiss to complete");
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsOpen);

            var compactPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_INSTANCES_COMPACT_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactPreviewPath) && width == 1024)
            {
                window.CaptureRenderedFrame()!.Save(compactPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactPreviewPath));
            }
        }
        else
        {
            Assert.Equal(0, instanceContentTranslation.X);
            Assert.True(instancesListPane!.IsHitTestVisible);
            Assert.True(instancesContent.IsHitTestVisible);
            Assert.True(managerWideActions.IsEffectivelyVisible);
            Assert.False(managerCompactSplitAction.IsEffectivelyVisible);
            Assert.False(compactInstanceGameIcon.IsEffectivelyVisible);
        }

        var instanceHub = instanceOverview?.FindControl<Grid>("InstanceHubScreen");
        var instanceSection = instancesView.FindControl<Grid>("InstanceSectionScreen");
        var managerInfoGroup = instancesView.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("managerInfoGroup"));
        Assert.NotNull(instanceHub);
        Assert.NotNull(instanceSection);
        Assert.True(managerInfoGroup.IsVisible);
        Assert.Equal(new CornerRadius(14), managerInfoGroup.CornerRadius);
        Assert.Equal(
            4,
            managerInfoGroup.GetVisualDescendants()
                .OfType<Border>()
                .Count(border => border.Classes.Contains("managerInfoCell")));
        Assert.All(
            managerInfoGroup.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("managerInfoCell")),
            cell => Assert.Equal(VerticalAlignment.Center, cell.Child?.VerticalAlignment));
        viewModel.SelectInstanceSectionCommand.Execute("mods");
        Assert.True(instanceHub!.IsVisible);
        Assert.Equal(usesCompactInstancesLayout, instanceSection!.IsVisible);
        await WaitForConditionAsync(
            () => instanceSection!.IsVisible &&
                  instanceSection.Opacity >= 0.99 &&
                  instanceSection.IsHitTestVisible,
            "instance section to open");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(usesCompactInstancesLayout, instanceHub!.IsVisible);
        Assert.True(instanceSection!.IsVisible);
        Assert.Contains("detailToolbar", instanceSection.GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.Classes.Contains("detailToolbar"))
            .Classes);
        var instanceSectionTitle = instanceSection.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(textBlock => textBlock.Classes.Contains("detailToolbarTitle"));
        var modsView = instancesView.FindControl<InstanceModsView>("InstanceModsContentView");
        var installedModsSection = modsView?.FindControl<Grid>("InstalledModsSection");
        Assert.NotNull(installedModsSection);
        Assert.Equal("Installed mods", instanceSectionTitle.Text);

        var sectionPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_INSTANCES_SECTION_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(sectionPreviewPath) && width == 1280)
        {
            window.CaptureRenderedFrame()!.Save(sectionPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(sectionPreviewPath));
        }

        viewModel.CloseInstanceSectionCommand.Execute(null);
        Assert.Equal(usesCompactInstancesLayout, instanceHub.IsVisible);
        Assert.True(instanceSection.IsVisible);
        Assert.True(installedModsSection!.IsVisible);
        Assert.Equal("Installed mods", instanceSectionTitle.Text);
        await WaitForConditionAsync(
            () => !instanceSection.IsVisible && instanceHub.IsVisible,
            "instance section to close");
        Dispatcher.UIThread.RunJobs();
        Assert.True(instanceHub.IsVisible);
        Assert.False(instanceSection.IsVisible);
        Assert.False(installedModsSection.IsVisible);

        var instancesPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_INSTANCES_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(instancesPreviewPath) && width == 1280)
        {
            window.CaptureRenderedFrame()!.Save(instancesPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(instancesPreviewPath));
        }

        var previewPath = Environment.GetEnvironmentVariable("HYPRISM_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath) && width == 1280)
        {
            frame.Save(previewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(previewPath));
        }

        if (width == 1920)
        {
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceToolbar.IsVisible);
            Assert.False(instancesContent.IsHitTestVisible);
            Assert.True(instanceContentTranslation.X > 0);
            Assert.Equal(
                Colors.Transparent,
                Assert.IsAssignableFrom<ISolidColorBrush>(addInstanceRow.Background).Color);

            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceToolbar.IsVisible);
            Assert.True(instancesContent.IsHitTestVisible);

            managedInstanceRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceToolbar.IsVisible);
            Assert.True(instancesContent.IsHitTestVisible);
            Assert.Equal(0, instanceContentTranslation.X);

            Assert.True(instancesView.TryCloseCompactContent());
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
        }

        viewModel.NavigateCommand.Execute("news");
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.HasFeaturedNews);
        Assert.Equal(3, viewModel.LatestNews.Count);
        news.Verify(
            service => service.GetNewsAsync(12),
            Times.Once);

        var newsLayout = FindVisualByName<Grid>(window, "NewsResponsiveLayout");
        var compactNewsShell = FindVisualByName<Grid>(window, "CompactNewsShell");
        var wideNewsShell = FindVisualByName<Grid>(window, "WideNewsShell");
        var wideNewsFeedBackground = FindVisualByName<Border>(window, "WideNewsFeedBackground");
        var compactArticleHost = FindVisualByName<ContentControl>(window, "CompactArticleHost");
        var wideArticleHost = FindVisualByName<ContentControl>(window, "WideArticleHost");
        Assert.NotNull(newsLayout);
        Assert.NotNull(compactNewsShell);
        Assert.NotNull(wideNewsShell);
        Assert.NotNull(wideNewsFeedBackground);
        Assert.NotNull(wideArticleHost);
        var compactArticleTranslation = Assert.IsType<TranslateTransform>(compactArticleHost!.RenderTransform);
        var compactArticleTransition = Assert.IsType<DoubleTransition>(Assert.Single(
            compactArticleTranslation.Transitions!,
            transition => transition is DoubleTransition { Property: { } property } &&
                          property == TranslateTransform.XProperty));
        Assert.Equal(TimeSpan.FromMilliseconds(300), compactArticleTransition.Duration);
        Assert.IsType<CubicEaseInOut>(compactArticleTransition.Easing);

        var usesWideLayout = newsLayout!.Bounds.Width >= 1180;
        Assert.Equal(!usesWideLayout, compactNewsShell!.IsVisible);
        Assert.Equal(usesWideLayout, wideNewsShell!.IsVisible);

        var activeNewsShell = usesWideLayout
            ? (Control)wideNewsShell
            : compactNewsShell;
        var newsListItems = activeNewsShell.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("newsListItem"))
            .ToArray();
        Assert.Equal(4, newsListItems.Length);
        Assert.All(newsListItems, item => Assert.InRange(item.Bounds.Height, 103, 105));
        Assert.All(newsListItems, item =>
        {
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(item.Background).Color.A);
            Assert.Equal(new Thickness(0), item.BorderThickness);
            Assert.Contains(
                item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                path => path.Classes.Contains("newsDateIcon"));

            var newsItem = Assert.IsType<NewsItemViewModel>(item.DataContext);
            var title = item.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == newsItem.Title);
            var date = item.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == newsItem.Date);
            var titleTop = title.TranslatePoint(default, item);
            var dateTop = date.TranslatePoint(default, item);
            Assert.NotNull(titleTop);
            Assert.NotNull(dateTop);
            Assert.Equal(new Thickness(0, 1, 0, 0), date.Margin);
            Assert.True(dateTop!.Value.Y > titleTop!.Value.Y);
        });
        if (width == 1280)
        {
            var hoverTarget = newsListItems[1];
            var hoverPoint = hoverTarget.TranslatePoint(
                new Point(hoverTarget.Bounds.Width / 2, hoverTarget.Bounds.Height / 2),
                window);
            Assert.NotNull(hoverPoint);
            window.MouseMove(hoverPoint!.Value);
            await WaitForConditionAsync(
                () => hoverTarget.Background is ISolidColorBrush { Color.A: >= 7 },
                "news list item hover background");
            Dispatcher.UIThread.RunJobs();
            var newsHoverColor = Assert.IsAssignableFrom<ISolidColorBrush>(hoverTarget.Background).Color;
            Assert.Equal(byte.MaxValue, newsHoverColor.R);
            Assert.Equal(byte.MaxValue, newsHoverColor.G);
            Assert.Equal(byte.MaxValue, newsHoverColor.B);
            Assert.InRange(newsHoverColor.A, (byte)7, (byte)8);
            Assert.NotNull(hoverTarget.Transitions);
        }
        Assert.DoesNotContain(
            activeNewsShell.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("newsFeaturedCard"));
        Assert.DoesNotContain(
            activeNewsShell.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => string.Equals(textBlock.Text, "Hytale", StringComparison.Ordinal) ||
                         string.Equals(textBlock.Text, "Latest", StringComparison.Ordinal));
        Assert.Equal(
            Color.Parse("#18191B"),
            Assert.IsAssignableFrom<ISolidColorBrush>(wideNewsFeedBackground!.Background).Color);
        var feedScrollViewer = activeNewsShell.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(scrollViewer => scrollViewer.Classes.Contains("newsFeedScroll"));
        Assert.Equal(ScrollBarVisibility.Auto, feedScrollViewer.VerticalScrollBarVisibility);
        var newsFeedItems = activeNewsShell.GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(panel => panel.Name == "NewsFeedItems");
        Assert.Equal(new Thickness(14, 18), newsFeedItems.Margin);
        var firstNewsItemPosition = newsListItems[0].TranslatePoint(default, feedScrollViewer);
        Assert.NotNull(firstNewsItemPosition);
        Assert.InRange(firstNewsItemPosition!.Value.X, 13.5, 14.5);
        for (var index = 1; index < newsListItems.Length; index++)
        {
            var previousPosition = newsListItems[index - 1].TranslatePoint(default, feedScrollViewer);
            var currentPosition = newsListItems[index].TranslatePoint(default, feedScrollViewer);
            Assert.NotNull(previousPosition);
            Assert.NotNull(currentPosition);
            var gap = currentPosition!.Value.Y -
                      (previousPosition!.Value.Y + newsListItems[index - 1].Bounds.Height);
            Assert.InRange(gap, 1.5, 2.5);
        }
        AssertUsesApplicationScrollBar(feedScrollViewer);

        await viewModel.FeaturedNews!.OpenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        if (!usesWideLayout)
        {
            compactArticleHost ??= FindVisualByName<ContentControl>(window, "CompactArticleHost");
            Assert.NotNull(compactArticleHost);
        }

        Assert.True(viewModel.IsNewsArticleVisible);
        Assert.False(viewModel.IsNewsFeedVisible);
        Assert.True(viewModel.FeaturedNews.IsSelected);
        Assert.Equal(usesWideLayout ? 0 : 1, viewModel.CompactNewsPageIndex);
        Assert.Equal("PRE-RELEASE PATCH NOTES (UPDATE 7)", viewModel.SelectedNewsArticle?.Title);
        var selectedArticle = viewModel.SelectedNewsArticle!;
        Assert.False(viewModel.IsNewsArticleSkeletonVisible);
        Assert.Equal(7, viewModel.SelectedNewsArticle?.Blocks.Count);
        Assert.Equal(selectedArticle.Blocks.Count, selectedArticle.RenderedBlocks.Count);
        Assert.Equal(
            "Hytale Team  ·  Game Update  ·  World Design  ·  05 Aug 2026",
            selectedArticle.Metadata);
        news.Verify(
            service => service.GetNewsArticleAsync(viewModel.FeaturedNews.Url),
            Times.Once);

        await viewModel.FeaturedNews.OpenCommand.ExecuteAsync(null);
        Assert.Same(selectedArticle, viewModel.SelectedNewsArticle);
        news.Verify(
            service => service.GetNewsArticleAsync(viewModel.FeaturedNews.Url),
            Times.Once);

        var selectedNewsButton = newsListItems.Single(button =>
            ReferenceEquals(button.DataContext, viewModel.FeaturedNews));
        await WaitForConditionAsync(
            () => selectedNewsButton.Background is ISolidColorBrush { Color: var color } &&
                  color == Color.Parse("#12FFFFFF"),
            "selected news item background");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            Color.Parse("#12FFFFFF"),
            Assert.IsAssignableFrom<ISolidColorBrush>(selectedNewsButton.Background).Color);

        var readerFrame = window.CaptureRenderedFrame();
        Assert.NotNull(readerFrame);
        Assert.Equal(new PixelSize(width, height), readerFrame!.PixelSize);

        var activeArticleHost = usesWideLayout ? wideArticleHost! : compactArticleHost!;
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<Border>(),
            border => border.IsEffectivelyVisible && border.Classes.Contains("skeleton"));
        var articleBody = activeArticleHost.GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(panel => panel.Classes.Contains("articleBody"));
        await WaitForConditionAsync(
            () => articleBody.Opacity >= 0.99,
            "news article reveal animation");
        Assert.Contains("revealed", articleBody.Classes);
        Assert.NotNull(articleBody.Transitions);
        Assert.InRange(articleBody.Opacity, 0.99, 1);
        Assert.Empty(articleBody.GetVisualDescendants().OfType<VirtualizingStackPanel>());
        Assert.InRange(
            articleBody.GetVisualDescendants().OfType<NewsRichTextBlock>().Count(),
            6,
            10);
        var articleScrollViewer = activeArticleHost.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(scrollViewer => scrollViewer.Classes.Contains("newsArticleScroll"));
        Assert.Equal(ScrollBarVisibility.Auto, articleScrollViewer.VerticalScrollBarVisibility);
        AssertUsesApplicationScrollBar(articleScrollViewer);
        Assert.Equal(
            usesWideLayout ? 0 : 1,
            activeArticleHost.GetVisualDescendants()
                .OfType<Button>()
                .Count(button => button.IsEffectivelyVisible && button.Classes.Contains("detailBack")));
        var articleHeader = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("newsArticleHeader"));
        Assert.Equal(new Thickness(0), articleHeader.BorderThickness);
        Assert.True(
            articleHeader.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("articleHeaderMask"))
                .IsEffectivelyVisible);
        var originalButton = activeArticleHost.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("articleAction"));
        Assert.Equal(new Thickness(0), originalButton.BorderThickness);
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(originalButton.Background).Color.A);
        if (usesWideLayout)
        {
            Assert.DoesNotContain(
                activeArticleHost.GetVisualDescendants().OfType<Border>(),
                border => border.IsEffectivelyVisible && border.Classes.Contains("detailToolbar"));
            Assert.Contains(originalButton, articleHeader.GetVisualDescendants());
            var headerTitle = articleHeader.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == selectedArticle.Title);
            var originalButtonPosition = originalButton.TranslatePoint(default, articleHeader);
            var headerTitlePosition = headerTitle.TranslatePoint(default, articleHeader);
            Assert.NotNull(originalButtonPosition);
            Assert.NotNull(headerTitlePosition);
            Assert.True(originalButtonPosition!.Value.Y < headerTitlePosition!.Value.Y);
            Assert.Equal(1, wideArticleHost!.Opacity);
        }
        else
        {
            var detailToolbar = activeArticleHost.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.IsEffectivelyVisible && border.Classes.Contains("detailToolbar"));
            Assert.Equal(new Thickness(0), detailToolbar.BorderThickness);
            Assert.InRange(detailToolbar.Margin.Left, 23.5, 24.5);
            Assert.InRange(detailToolbar.Bounds.Height, 55.5, 56.5);
            var toolbarPosition = detailToolbar.TranslatePoint(default, activeArticleHost);
            var headerPosition = articleHeader.TranslatePoint(default, activeArticleHost);
            Assert.NotNull(toolbarPosition);
            Assert.NotNull(headerPosition);
            Assert.InRange(
                Math.Abs(toolbarPosition!.Value.X - headerPosition!.Value.X),
                0,
                6);
            var backButton = detailToolbar.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("detailBack"));
            Assert.Equal(new Thickness(0), backButton.BorderThickness);
            Assert.Equal(
                0,
                Assert.IsAssignableFrom<ISolidColorBrush>(backButton.Background).Color.A);
            var toolbarTitle = detailToolbar.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Classes.Contains("detailToolbarTitle"));
            Assert.Equal(0, toolbarTitle.Opacity);
            Assert.Equal(16, toolbarTitle.FontSize);
            var backIcon = backButton.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Path>()
                .Single();
            Assert.Equal(12, backIcon.Width);
            Assert.Equal(12, backIcon.Height);
            var initialBackPosition = backButton.TranslatePoint(default, activeArticleHost);
            var initialOriginalPosition = originalButton.TranslatePoint(default, activeArticleHost);
            Assert.NotNull(initialBackPosition);
            Assert.NotNull(initialOriginalPosition);

            articleScrollViewer.Offset = new Vector(0, 54);
            Dispatcher.UIThread.RunJobs();
            await WaitForConditionAsync(
                () => viewModel.IsNewsArticleScrolled && toolbarTitle.Opacity >= 0.99,
                "compact news toolbar to reveal its title");
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsNewsArticleScrolled);
            Assert.InRange(detailToolbar.Margin.Left, 23.5, 24.5);
            Assert.InRange(toolbarTitle.Opacity, 0.99, 1);
            Assert.Equal(selectedArticle.Title, toolbarTitle.Text);
            Assert.True(toolbarTitle.Bounds.Width > 280);
            var scrolledBackPosition = backButton.TranslatePoint(default, activeArticleHost);
            var scrolledOriginalPosition = originalButton.TranslatePoint(default, activeArticleHost);
            var toolbarTitlePosition = toolbarTitle.TranslatePoint(default, detailToolbar);
            Assert.NotNull(scrolledBackPosition);
            Assert.NotNull(scrolledOriginalPosition);
            Assert.NotNull(toolbarTitlePosition);
            Assert.InRange(
                Math.Abs(scrolledBackPosition!.Value.X - initialBackPosition!.Value.X),
                0,
                0.5);
            Assert.InRange(
                Math.Abs(scrolledOriginalPosition!.Value.X - initialOriginalPosition!.Value.X),
                0,
                0.5);
            Assert.InRange(
                toolbarTitlePosition!.Value.X + toolbarTitle.Bounds.Width / 2,
                detailToolbar.Bounds.Width / 2 - 1,
                detailToolbar.Bounds.Width / 2 + 1);
        }
        var articleActionHoverPoint = originalButton.TranslatePoint(
            new Point(originalButton.Bounds.Width / 2, originalButton.Bounds.Height / 2),
            window);
        Assert.NotNull(articleActionHoverPoint);
        window.MouseMove(articleActionHoverPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(originalButton.Background).Color.A);
        Assert.NotNull(originalButton.Transitions);
        var contentHeading = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                               control.Classes.Contains("articleHeading"));
        Assert.True(double.IsNaN(contentHeading.LineHeight));
        Assert.Contains(
            contentHeading.Inlines!.OfType<Run>(),
            run => run.Text == "Creative Gameplay Quality");
        var inlineCode = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .First(border =>
                border.IsEffectivelyVisible &&
                border.Classes.Contains("inlineCode") &&
                border.Child is TextBlock { Text: "worldgen.reload()" });
        var inlineCodeText = Assert.IsType<TextBlock>(inlineCode.Child);
        Assert.Contains("JetBrains Mono", inlineCodeText.FontFamily.Name);
        Assert.Equal(18, inlineCodeText.LineHeight);
        Assert.Equal(new CornerRadius(6), inlineCode.CornerRadius);
        var blockCode = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .First(border =>
                border.IsEffectivelyVisible &&
                border.Classes.Contains("articleCode"));
        var blockCodeText = Assert.IsType<TextBlock>(blockCode.Child);
        Assert.Contains("JetBrains Mono", blockCodeText.FontFamily.Name);
        Assert.Equal(new CornerRadius(10), blockCode.CornerRadius);
        Assert.Contains(
            activeArticleHost.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.IsEffectivelyVisible &&
                         textBlock.Text == selectedArticle.Metadata);
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.IsEffectivelyVisible &&
                         textBlock.Text == "A closer look at the world and its creatures.");

        var detailsButton = activeArticleHost.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible &&
                              button.Classes.Contains("articleDetailsHeader"));
        var detailsBlock = Assert.IsType<NewsArticleBlockViewModel>(detailsButton.DataContext);
        Assert.False(detailsBlock.IsDetailsExpanded);
        var detailsPanel = Assert.IsType<StackPanel>(detailsButton.Parent);
        var detailsContainer = Assert.IsType<Border>(detailsPanel.Parent);
        Assert.Equal(
            detailsContainer.Bounds.Width -
                detailsContainer.BorderThickness.Left -
                detailsContainer.BorderThickness.Right,
            detailsButton.Bounds.Width,
            0.5);
        var detailsLabel = detailsButton.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.Classes.Contains("articleDetailsSummary"));
        var detailsEmote = detailsButton.GetVisualDescendants()
            .OfType<Image>()
            .Single(image => image.Width == 24 && image.Height == 24);
        var detailsLabelPosition = detailsLabel.TranslatePoint(default, detailsButton);
        var detailsEmotePosition = detailsEmote.TranslatePoint(default, detailsButton);
        await WaitForConditionAsync(
            () =>
            {
                var currentLabelPosition = detailsLabel.TranslatePoint(default, detailsButton);
                var currentEmotePosition = detailsEmote.TranslatePoint(default, detailsButton);
                if (currentLabelPosition is null || currentEmotePosition is null)
                    return false;

                var gap = currentEmotePosition.Value.X -
                    (currentLabelPosition.Value.X + detailsLabel.Bounds.Width);
                return gap is >= 6.5 and <= 7.5;
            },
            "article details summary layout to settle");
        detailsLabelPosition = detailsLabel.TranslatePoint(default, detailsButton);
        detailsEmotePosition = detailsEmote.TranslatePoint(default, detailsButton);
        Assert.NotNull(detailsLabelPosition);
        Assert.NotNull(detailsEmotePosition);
        var detailsLabelCenter = detailsLabelPosition!.Value.Y + detailsLabel.Bounds.Height / 2;
        var detailsEmoteCenter = detailsEmotePosition!.Value.Y + detailsEmote.Bounds.Height / 2;
        Assert.InRange(Math.Abs(detailsLabelCenter - detailsEmoteCenter), 0, 0.5);
        Assert.InRange(
            detailsEmotePosition.Value.X -
                (detailsLabelPosition.Value.X + detailsLabel.Bounds.Width),
            6.5,
            7.5);
        if (width > 1024)
        {
            var detailsHoverPoint = detailsButton.TranslatePoint(
                new Point(detailsButton.Bounds.Width * 0.6, detailsButton.Bounds.Height / 2),
                window);
            Assert.NotNull(detailsHoverPoint);
            window.MouseMove(detailsHoverPoint!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(detailsButton.IsPointerOver);
            Assert.Equal(
                Color.Parse("#08FFFFFF"),
                Assert.IsAssignableFrom<ISolidColorBrush>(detailsButton.Background).Color);
        }
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<NewsRichTextBlock>(),
            control => control.Inlines?.OfType<Run>().Any(run =>
                           run.Text == "Hidden implementation notes.") == true);
        detailsButton.Command!.Execute(detailsButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(detailsBlock.IsDetailsExpanded);
        Assert.Contains(
            activeArticleHost.GetVisualDescendants().OfType<NewsRichTextBlock>(),
            control => control.IsEffectivelyVisible &&
                       control.Inlines?.OfType<Run>().Any(run =>
                           run.Text == "Hidden implementation notes.") == true);

        var quote = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("articleQuote") && border.IsVisible);
        var quoteText = quote.GetVisualDescendants().OfType<NewsRichTextBlock>().Single();
        Assert.NotNull(quoteText.Inlines);
        Assert.IsNotType<LineBreak>(quoteText.Inlines!.Last());
        var articleText = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                control.Inlines?.OfType<Run>().Any(run => run.Text == "Hytale News") == true);
        Assert.NotNull(articleText.Inlines);
        var articleInlines = articleText.Inlines!;
        var articleLink = articleInlines.OfType<Run>()
            .Single(run => run.Text == "Hytale News");
        var articleLinkForeground = Assert.IsType<SolidColorBrush>(articleLink.Foreground);
        var articleLinkUnderline = Assert.IsType<SolidColorBrush>(
            Assert.Single(articleLink.TextDecorations!).Stroke);
        Assert.Equal(Color.Parse("#C9BCFF"), articleLinkForeground.Color);
        Assert.Equal(0, articleLinkUnderline.Opacity);

        var linkedListText = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                control.Inlines?.OfType<Run>().Any(run =>
                    run.Text == "https://hytalemodding.dev/") == true);
        var linkedListRuns = linkedListText.Inlines!.OfType<Run>().ToArray();
        var linkedListLinkIndex = Array.FindIndex(linkedListRuns, run =>
            run.Text == "https://hytalemodding.dev/");
        Assert.True(linkedListLinkIndex > 0);
        Assert.EndsWith(" ", linkedListRuns[linkedListLinkIndex - 1].Text);
        Assert.DoesNotContain(linkedListText.Inlines!, inline => inline is InlineUIContainer);
        var parentListText = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                control.Inlines?.OfType<Run>().Any(run =>
                    run.Text == "Customize items further") == true);
        var parentListPoint = parentListText.TranslatePoint(default, activeArticleHost);
        var nestedListPoint = linkedListText.TranslatePoint(default, activeArticleHost);
        Assert.NotNull(parentListPoint);
        Assert.NotNull(nestedListPoint);
        Assert.True(nestedListPoint!.Value.X >= parentListPoint!.Value.X + 28);

        var nestedListRow = Assert.IsType<Grid>(linkedListText.Parent);
        var nestedMarker = nestedListRow.Children
            .OfType<Avalonia.Controls.Shapes.Ellipse>()
            .Single(ellipse => ellipse.IsVisible);
        Assert.Equal(5, nestedMarker.Width);
        Assert.Equal(5, nestedMarker.Height);
        Assert.Equal(new Thickness(1, 11.5, 0, 0), nestedMarker.Margin);

        var linkStart = 0;
        foreach (var inline in articleInlines)
        {
            if (ReferenceEquals(inline, articleLink))
                break;
            linkStart += inline switch
            {
                Run run => run.Text?.Length ?? 0,
                LineBreak => 1,
                InlineUIContainer => 1,
                _ => 0
            };
        }

        var linkBounds = articleText.TextLayout.HitTestTextPosition(linkStart);
        var linkPoint = articleText.TranslatePoint(
            new Point(
                linkBounds.X + Math.Min(4, linkBounds.Width / 2),
                linkBounds.Y + linkBounds.Height / 2),
            window);
        Assert.NotNull(linkPoint);
        window.MouseMove(linkPoint!.Value);
        await WaitForConditionAsync(
            () => articleLinkUnderline.Opacity >= 0.99 &&
                  articleLinkForeground.Color == Color.Parse("#E0D8FF"),
            "article link hover style");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), articleText.Cursor?.ToString());
        Assert.InRange(articleLinkUnderline.Opacity, 0.99, 1);
        Assert.Equal(Color.Parse("#E0D8FF"), articleLinkForeground.Color);

        window.MouseDown(linkPoint.Value, MouseButton.Left);
        window.MouseUp(linkPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytale.com/news"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var nestedLinkRun = linkedListRuns[linkedListLinkIndex];
        var nestedUnderline = Assert.IsType<SolidColorBrush>(
            Assert.Single(nestedLinkRun.TextDecorations!).Stroke);
        var nestedLinkStart = linkedListRuns
            .Take(linkedListLinkIndex)
            .Sum(run => run.Text?.Length ?? 0);
        var nestedLinkBounds = linkedListText.TextLayout.HitTestTextPosition(nestedLinkStart);
        var nestedLinkPoint = linkedListText.TranslatePoint(
            new Point(
                nestedLinkBounds.X + Math.Min(4, nestedLinkBounds.Width / 2),
                nestedLinkBounds.Y + nestedLinkBounds.Height / 2),
            window);
        Assert.NotNull(nestedLinkPoint);
        window.MouseMove(nestedLinkPoint!.Value);
        await WaitForConditionAsync(
            () => nestedUnderline.Opacity >= 0.99,
            "nested article link hover style");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), linkedListText.Cursor?.ToString());
        Assert.InRange(nestedUnderline.Opacity, 0.99, 1);
        window.MouseDown(nestedLinkPoint.Value, MouseButton.Left);
        window.MouseUp(nestedLinkPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytalemodding.dev/"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var policyLinkIndex = Array.FindIndex(linkedListRuns, run =>
            run.Text == "Server Owner Policies");
        Assert.True(policyLinkIndex > linkedListLinkIndex);
        var policyLinkRun = linkedListRuns[policyLinkIndex];
        var policyUnderline = Assert.IsType<SolidColorBrush>(
            Assert.Single(policyLinkRun.TextDecorations!).Stroke);
        var policyLinkStart = linkedListRuns
            .Take(policyLinkIndex)
            .Sum(run => run.Text?.Length ?? 0);
        var policyLinkBounds = linkedListText.TextLayout.HitTestTextPosition(policyLinkStart);
        var policyLinkPoint = linkedListText.TranslatePoint(
            new Point(
                policyLinkBounds.X + Math.Min(4, policyLinkBounds.Width / 2),
                policyLinkBounds.Y + policyLinkBounds.Height / 2),
            window);
        Assert.NotNull(policyLinkPoint);
        window.MouseMove(policyLinkPoint!.Value);
        await WaitForConditionAsync(
            () => policyUnderline.Opacity >= 0.99,
            "policy article link hover style");
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(policyUnderline.Opacity, 0.99, 1);
        window.MouseDown(policyLinkPoint.Value, MouseButton.Left);
        window.MouseUp(policyLinkPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytale.com/server-policies"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var readerPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_READER_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(readerPreviewPath) && width == 1280)
        {
            readerFrame.Save(readerPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(readerPreviewPath));
        }

        var compactReaderPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_READER_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactReaderPreviewPath) && width == 1024)
        {
            readerFrame.Save(compactReaderPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactReaderPreviewPath));
        }

        var wideReaderPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_READER_WIDE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(wideReaderPreviewPath) && width == 1920)
        {
            readerFrame.Save(wideReaderPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(wideReaderPreviewPath));
        }

        if (usesWideLayout)
        {
            var nextNewsItem = Assert.IsType<NewsItemViewModel>(newsListItems[1].DataContext);
            var readerDisappeared = false;
            PropertyChangedEventHandler readerVisibilityObserver = (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.SelectedNewsArticle) &&
                    viewModel.SelectedNewsArticle is null)
                {
                    readerDisappeared = true;
                }
            };
            viewModel.PropertyChanged += readerVisibilityObserver;
            var switchTask = nextNewsItem.OpenCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsNewsArticleVisible);
            Assert.True(viewModel.IsNewsArticleBodySkeletonVisible);
            Assert.False(viewModel.IsNewsArticleBodyVisible);
            Assert.Equal(nextNewsItem.Title, viewModel.NewsArticleDisplayTitle);
            Assert.NotNull(viewModel.SelectedNewsArticle);
            Assert.True(activeArticleHost.GetVisualDescendants()
                .OfType<StackPanel>()
                .Single(panel => panel.Name == "ArticleBodySkeleton")
                .IsEffectivelyVisible);

            await switchTask;
            viewModel.PropertyChanged -= readerVisibilityObserver;
            Dispatcher.UIThread.RunJobs();
            Assert.False(readerDisappeared);
            Assert.Same(nextNewsItem, viewModel.SelectedNewsItem);
            Assert.NotSame(selectedArticle, viewModel.SelectedNewsArticle);
            Assert.True(viewModel.IsNewsArticleBodyVisible);
            Assert.False(viewModel.IsNewsArticleBodySkeletonVisible);
        }

        var closeTask = viewModel.CloseNewsArticleCommand.ExecuteAsync(null);
        if (!usesWideLayout)
        {
            Assert.Same(selectedArticle, viewModel.SelectedNewsArticle);
            Assert.Equal(0, viewModel.CompactNewsPageIndex);
            Assert.DoesNotContain(
                activeArticleHost.GetVisualDescendants().OfType<TextBlock>(),
                textBlock => textBlock.IsEffectivelyVisible &&
                             textBlock.Text == viewModel.SelectArticleLabel);
        }
        await closeTask;
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsNewsFeedVisible);
        Assert.Null(viewModel.SelectedNewsArticle);
        Assert.False(viewModel.FeaturedNews.IsSelected);
        Assert.Equal(0, viewModel.CompactNewsPageIndex);
        if (!usesWideLayout)
            Assert.True(compactArticleTranslation.X > 0);

        await viewModel.FeaturedNews.OpenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(selectedArticle, viewModel.SelectedNewsArticle);
        Assert.False(viewModel.IsNewsArticleSkeletonVisible);
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<Border>(),
            border => border.IsEffectivelyVisible && border.Classes.Contains("skeleton"));
        news.Verify(
            service => service.GetNewsArticleAsync(viewModel.FeaturedNews.Url),
            Times.Once);
        await viewModel.CloseNewsArticleCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var newsPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_NEWS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(newsPreviewPath) && width == 1280)
        {
            var newsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(newsFrame);
            newsFrame!.Save(newsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(newsPreviewPath));
        }

        var compactNewsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_NEWS_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactNewsPreviewPath) && width == 1024)
        {
            var newsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(newsFrame);
            newsFrame!.Save(compactNewsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactNewsPreviewPath));
        }

        var wideNewsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_NEWS_WIDE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(wideNewsPreviewPath) && width == 1920)
        {
            var newsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(newsFrame);
            newsFrame!.Save(wideNewsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(wideNewsPreviewPath));
        }

        viewModel.NavigateCommand.Execute("settings");
        Dispatcher.UIThread.RunJobs();

        viewModel.Settings.ShowAddJavaArgumentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Thickness(1, 1, 1, 0), mainSceneFrame.BorderThickness);
        viewModel.Settings.CancelAddJavaArgumentCommand.Execute(null);
        await AvaloniaTestWait.UntilAsync(
            () => mainSceneFrame.BorderThickness == new Thickness(1),
            "frame border after Java argument modal closes");
        viewModel.Settings.ShowAddEnvironmentVariableCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Thickness(1, 1, 1, 0), mainSceneFrame.BorderThickness);
        viewModel.Settings.CancelAddEnvironmentVariableCommand.Execute(null);
        await AvaloniaTestWait.UntilAsync(
            () => mainSceneFrame.BorderThickness == new Thickness(1),
            "frame border after environment variable modal closes");
        viewModel.Settings.ShowAddAuthServerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Thickness(1, 1, 1, 0), mainSceneFrame.BorderThickness);
        viewModel.Settings.CancelAddAuthServerCommand.Execute(null);
        await AvaloniaTestWait.UntilAsync(
            () => mainSceneFrame.BorderThickness == new Thickness(1),
            "frame border after auth server modal closes");

        var settingsView = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var settingsScroll = settingsView.FindControl<ScrollViewer>("SettingsContent");
        var categoryScroll = settingsView.FindControl<ScrollViewer>("SettingsCategoryScroll");
        var settingsRail = settingsView.FindControl<Border>("SettingsCategoryRail");
        var compactSettingsToolbar = settingsView.FindControl<Border>("CompactSettingsToolbar");
        var compactSettingsTitle = settingsView.FindControl<TextBlock>("CompactSettingsTitle");
        var settingsMain = settingsView.FindControl<Grid>("SettingsMain");
        Assert.NotNull(settingsScroll);
        Assert.NotNull(categoryScroll);
        Assert.NotNull(settingsRail);
        Assert.NotNull(compactSettingsToolbar);
        Assert.NotNull(compactSettingsTitle);
        Assert.NotNull(settingsMain);
        AssertUsesApplicationScrollBar(categoryScroll!);
        AssertUsesApplicationScrollBar(settingsScroll!);
        var settingsToggles = settingsView.GetVisualDescendants().OfType<ToggleSwitch>().ToArray();
        Assert.NotEmpty(settingsToggles);
        Assert.All(settingsToggles, toggle =>
        {
            Assert.Null(toggle.OnContent);
            Assert.Null(toggle.OffContent);
            Assert.Equal(new Thickness(0), toggle.BorderThickness);
            Assert.Equal(48, toggle.Width);
        });
        Assert.All(settingsToggles.Where(toggle => toggle.IsEffectivelyVisible), toggle =>
        {
            var track = toggle.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "SettingsSwitchTrack");
            var movingKnobs = toggle.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Name == "PART_MovingKnobs");
            var knob = toggle.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>()
                .Single(ellipse => ellipse.Name == "SettingsSwitchKnob");
            Assert.Equal(new Thickness(0), track.BorderThickness);
            Assert.Equal(new CornerRadius(13), track.CornerRadius);
            Assert.Equal(18, knob.Width);
            Assert.Equal(18, knob.Height);
            var movement = Assert.IsType<DoubleTransition>(Assert.Single(
                movingKnobs.Transitions!,
                transition => transition is DoubleTransition { Property: { } property } &&
                              property == Canvas.LeftProperty));
            Assert.Equal(TimeSpan.FromMilliseconds(220), movement.Duration);
            Assert.IsType<CubicEaseInOut>(movement.Easing);
        });
        if (width == 1280)
        {
            var toggle = settingsToggles.First(item => item.IsEffectivelyVisible);
            var track = toggle.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "SettingsSwitchTrack");
            Assert.Equal(
                Color.Parse("#303237"),
                Assert.IsAssignableFrom<ISolidColorBrush>(track.Background).Color);
            toggle.IsChecked = true;
            await WaitForConditionAsync(
                () => track.Background is ISolidColorBrush { Color: var color } &&
                      color == Color.Parse("#35A85B"),
                "settings toggle track color");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                Color.Parse("#35A85B"),
                Assert.IsAssignableFrom<ISolidColorBrush>(track.Background).Color);
            toggle.IsChecked = false;
        }
        var settingsComboBoxes = settingsView.GetVisualDescendants().OfType<ComboBox>().ToArray();
        Assert.NotEmpty(settingsComboBoxes);
        Assert.All(settingsComboBoxes, comboBox =>
        {
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, comboBox.HorizontalAlignment);
            Assert.Equal(new Thickness(0), comboBox.BorderThickness);
            Assert.Equal(new CornerRadius(11), comboBox.CornerRadius);
            Assert.NotNull(comboBox.ItemContainerTheme);
        });
        viewModel.Settings.ShowAddJavaArgumentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var javaArgumentOverlay = settingsView.FindControl<OverlayModal>("JavaArgumentModal");
        Assert.NotNull(javaArgumentOverlay);
        Assert.True(javaArgumentOverlay.IsEffectivelyVisible);
        var settingsTextBoxes = javaArgumentOverlay.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.NotEmpty(settingsTextBoxes);
        Assert.All(settingsTextBoxes, textBox =>
        {
            Assert.Equal(new Thickness(0), textBox.BorderThickness);
            Assert.Equal(new CornerRadius(11), textBox.CornerRadius);
            Assert.Null(textBox.FocusAdorner);
        });
        viewModel.Settings.CancelAddJavaArgumentCommand.Execute(null);
        var compactSettingsLayout = settingsView.Bounds.Width < 940;
        Assert.True(settingsRail!.IsEffectivelyVisible);
        Assert.Equal(compactSettingsLayout, compactSettingsToolbar!.IsVisible);
        Assert.Equal(!compactSettingsLayout, settingsMain!.IsHitTestVisible);
        var categoryIcons = settingsView.GetVisualDescendants()
            .OfType<Image>()
            .Where(image => image.Classes.Contains("settingsCategoryIcon"))
            .ToArray();
        Assert.Equal(7, categoryIcons.Length);
        Assert.All(categoryIcons, icon => Assert.NotNull(icon.Source));
        Assert.Equal(7, categoryIcons.Count(icon => icon.IsEffectivelyVisible));
        var categoryDescriptions = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.Classes.Contains("managerCategoryDescription"))
            .ToArray();
        Assert.Equal(7, categoryDescriptions.Length);
        Assert.Equal(7, categoryDescriptions.Count(description => description.IsEffectivelyVisible));
        var categoryTitles = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.Classes.Contains("settingsCategoryTitle"))
            .ToArray();
        Assert.Equal(7, categoryTitles.Length);
        Assert.All(
            categoryTitles,
            title => Assert.Equal(
                Color.Parse("#F7F7F8"),
                Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color));
        var categoryButtons = settingsView.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("managerRailCategory"))
            .ToArray();
        Assert.Equal(7, categoryButtons.Length);
        Assert.All(
            categoryButtons,
            button => Assert.InRange(
                Math.Abs(button.Bounds.Width - (categoryScroll!.Viewport.Width - 10)),
                0,
                1));
        if (compactSettingsLayout)
        {
            window.MouseMove(new Point(0, 0));
            await WaitForConditionAsync(
                () => settingsRail.Background is ISolidColorBrush { Color: var color } &&
                      color == Color.Parse("#0D0E10"),
                "compact settings rail background");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                Color.Parse("#0D0E10"),
                Assert.IsAssignableFrom<ISolidColorBrush>(settingsRail.Background).Color);
            var selectedCategoryButton = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("managerRailCategory") &&
                                  button.Classes.Contains("selected"));
            await WaitForConditionAsync(
                () => selectedCategoryButton.Background is ISolidColorBrush { Color.A: <= 1 },
                "compact selected category hover fade");
            Assert.InRange(
                Assert.IsAssignableFrom<ISolidColorBrush>(selectedCategoryButton.Background).Color.A,
                (byte)0,
                (byte)1);
            var selectedCategoryPoint = selectedCategoryButton.TranslatePoint(
                new Point(
                    selectedCategoryButton.Bounds.Width / 2,
                    selectedCategoryButton.Bounds.Height / 2),
                window);
            Assert.NotNull(selectedCategoryPoint);
            window.MouseMove(selectedCategoryPoint!.Value);
            await WaitForConditionAsync(
                () => selectedCategoryButton.Background is ISolidColorBrush { Color.A: >= 7 },
                "selected settings category hover color");
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(
                Assert.IsAssignableFrom<ISolidColorBrush>(selectedCategoryButton.Background).Color.A,
                (byte)7,
                (byte)8);
            AssertNoPressScale(selectedCategoryButton);
            window.MouseMove(new Point(0, 0));
            await WaitForConditionAsync(
                () => selectedCategoryButton.Background is ISolidColorBrush { Color.A: <= 1 },
                "selected settings category hover color to clear");
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(
                Assert.IsAssignableFrom<ISolidColorBrush>(selectedCategoryButton.Background).Color.A,
                (byte)0,
                (byte)1);

            var categoryScrollBar = categoryScroll.GetVisualDescendants()
                .OfType<ScrollBar>()
                .Single(scrollBar => scrollBar.Orientation == Avalonia.Layout.Orientation.Vertical);
            // Seven merged categories may fit the rail without scrolling, so the
            // hover-expanded scrollbar checks only apply when it is scrollable
            if (categoryScrollBar.IsVisible)
            {
                var categoryScrollThumb = categoryScrollBar.GetVisualDescendants().OfType<Thumb>().Single();
                var scrollBarPoint = categoryScrollThumb.TranslatePoint(
                    new Point(categoryScrollThumb.Bounds.Width / 2, categoryScrollThumb.Bounds.Height / 2),
                    window);
                Assert.NotNull(scrollBarPoint);
                window.MouseMove(scrollBarPoint!.Value);
                await WaitForConditionAsync(
                    () => categoryScrollBar.IsExpanded && categoryScrollThumb.Width >= 5.99,
                    "settings category scroll bar to expand");
                Dispatcher.UIThread.RunJobs();
                Assert.True(categoryScrollBar.IsExpanded);
                var expandedThumb = categoryScrollBar.GetVisualDescendants().OfType<Thumb>().Single();
                Assert.InRange(expandedThumb.Width, 5.99, 6.01);
                Assert.InRange(expandedThumb.Bounds.Width, 5.5, 6.5);
                var expandedThumbCenter = expandedThumb.TranslatePoint(
                    new Point(expandedThumb.Bounds.Width / 2, expandedThumb.Bounds.Height / 2),
                    categoryScrollBar);
                Assert.NotNull(expandedThumbCenter);
                Assert.InRange(
                    Math.Abs(expandedThumbCenter!.Value.X - (categoryScrollBar.Bounds.Width / 2)),
                    0,
                    1);
            }

            var expandedScrollBarPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_SETTINGS_SCROLLBAR_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(expandedScrollBarPreviewPath) && width == 1024)
            {
                var settingsFrame = window.CaptureRenderedFrame();
                Assert.NotNull(settingsFrame);
                settingsFrame!.Save(expandedScrollBarPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(expandedScrollBarPreviewPath));
            }

            window.MouseMove(new Point(0, 0));
        }
        else
        {
            Assert.Equal(276, settingsRail.Bounds.Width);
            Assert.Equal(
                Color.Parse("#18191B"),
                Assert.IsAssignableFrom<ISolidColorBrush>(settingsRail.Background).Color);
        }
        var visibleSettingsGroups = settingsView.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("formGroup"))
            .ToArray();
        Assert.Equal(4, visibleSettingsGroups.Length);
        Assert.All(visibleSettingsGroups, group =>
        {
            Assert.Equal(new Thickness(0), group.BorderThickness);
            Assert.Equal(
                Color.Parse("#151618"),
                Assert.IsAssignableFrom<ISolidColorBrush>(group.Background).Color);
            Assert.Equal(new CornerRadius(14), group.CornerRadius);

            var rows = group.GetVisualDescendants()
                .OfType<Visual>()
                .Where(control =>
                    control.IsEffectivelyVisible &&
                    (control is FormRow || (control is Border && control.Classes.Contains("formRow"))))
                .ToArray();
            if (rows.Length == 0)
                return;

            Assert.All(rows.Where(row => !row.Classes.Contains("last")), row =>
            {
                var borderThickness = row switch
                {
                    FormRow settingsRow => settingsRow.BorderThickness,
                    Border border => border.BorderThickness,
                    _ => default(Thickness)
                };
                var borderBrush = row switch
                {
                    FormRow settingsRow => settingsRow.BorderBrush,
                    Border border => border.BorderBrush,
                    _ => null
                };
                Assert.Equal(new Thickness(0, 0, 0, 3), borderThickness);
                Assert.Equal(
                    Color.Parse("#0D0E10"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(borderBrush).Color);
            });
        });
        var groupsWithRows = visibleSettingsGroups
            .Count(group => group.GetVisualDescendants()
                .OfType<Visual>()
                .Any(control => control.IsEffectivelyVisible &&
                                (control is FormRow || (control is Border && control.Classes.Contains("formRow")))));
        Assert.Equal(3, groupsWithRows);
        var visibleSettingsHeadings = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsCategoryHeading"))
            .ToArray();
        Assert.Equal(4, visibleSettingsHeadings.Length);
        Assert.Contains(visibleSettingsHeadings, heading => heading.Text == viewModel.Settings.LanguageCategoryTitle);
        Assert.Contains(visibleSettingsHeadings, heading => heading.Text == viewModel.Settings.GeneralTitle);
        Assert.Contains(visibleSettingsHeadings, heading => heading.Text == viewModel.Settings.GpuLabel);
        Assert.Contains(visibleSettingsHeadings, heading => heading.Text == viewModel.Settings.EnvLabel);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Name == "SettingsHeader");
        Assert.Equal(7, viewModel.Settings.Categories.Count);
        Assert.DoesNotContain(viewModel.Settings.Categories, category => category.Id == "developer");
        Assert.True(viewModel.Settings.IsGeneral);
        Assert.All(categoryButtons, AssertNoPressScale);

        var javaCategory = viewModel.Settings.Categories.Single(category => category.Id == "java");
        viewModel.Settings.SelectCategoryCommand.Execute(javaCategory);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            3,
            settingsView.GetVisualDescendants().OfType<Border>().Count(
                border => border.IsEffectivelyVisible && border.Classes.Contains("formGroup")));
        var javaCategoryHeadings = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsCategoryHeading"))
            .Select(text => text.Text)
            .ToArray();
        Assert.Contains(viewModel.Settings.JavaRuntimeLabel, javaCategoryHeadings);
        Assert.Contains(viewModel.Settings.RamAllocationLabel, javaCategoryHeadings);
        Assert.Contains(viewModel.Settings.JavaArgumentsLabel, javaCategoryHeadings);
        var javaView = Assert.Single(settingsView.GetVisualDescendants().OfType<SettingsJavaView>());
        var javaArgumentsTable = javaView.FindControl<Border>("JavaArgumentsTable");
        var addJavaArgumentButton = javaView.FindControl<Button>("AddJavaArgumentButton");
        var javaArgumentModal = settingsView.FindControl<OverlayModal>("JavaArgumentModal");
        Assert.NotNull(javaArgumentsTable);
        Assert.NotNull(addJavaArgumentButton);
        Assert.NotNull(javaArgumentModal);
        Assert.Same(viewModel.Settings.ShowAddJavaArgumentCommand, addJavaArgumentButton!.Command);
        viewModel.Settings.ShowAddJavaArgumentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(javaArgumentModal!.IsOpen);
        Assert.True(javaArgumentModal.IsVisible);
        var modalForm = Assert.Single(javaArgumentModal.GetVisualDescendants().OfType<ModalForm>());
        Assert.Equal(viewModel.Settings.JavaArgumentsLabel, modalForm.Title);
        Assert.Equal(viewModel.Settings.JavaArgumentsHint, modalForm.Description);
        window.UpdateLayout();
        var javaModalSheet = javaArgumentModal.FindControl<Grid>("OverlayModalSheet");
        var javaModalShoulders = javaArgumentModal.FindControl<Grid>("OverlayModalShoulders");
        var javaModalShoulderMask = javaArgumentModal.FindControl<Border>("OverlayModalShoulderMask");
        Assert.NotNull(javaModalSheet);
        Assert.NotNull(javaModalShoulders);
        Assert.NotNull(javaModalShoulderMask);
        await WaitForConditionAsync(
            () => Assert.IsType<TranslateTransform>(javaModalSheet!.RenderTransform).Y == 0 &&
                  Assert.IsType<ScaleTransform>(javaModalShoulders!.RenderTransform).ScaleY == 1,
            "Java argument modal to finish opening");
        var javaArgumentInput = Assert.Single(javaArgumentModal.GetVisualDescendants().OfType<TextBox>());
        await WaitForConditionAsync(
            () => ReferenceEquals(window.FocusManager?.GetFocusedElement(), javaArgumentInput),
            "Java argument modal initial focus");
        var javaArgumentField = Assert.Single(javaArgumentModal.GetVisualDescendants().OfType<ModalTextField>());
        Assert.Same(javaArgumentInput, javaArgumentField.Content);
        Assert.Equal(new Thickness(12, 9, 44, 9), javaArgumentInput.Padding);
        viewModel.Settings.JavaArgumentsError = "Invalid Java argument";
        Dispatcher.UIThread.RunJobs();
        Assert.True(javaArgumentField.IsError);
        var javaArgumentErrorIcon = Assert.Single(
            javaArgumentField.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("modalTextFieldError"));
        Assert.True(javaArgumentErrorIcon.IsVisible);
        Assert.Equal(
            Color.Parse("#322525"),
            Assert.IsAssignableFrom<ISolidColorBrush>(javaArgumentInput.Background).Color);
        var javaArgumentInputBorder = Assert.Single(
            javaArgumentInput.GetVisualDescendants().OfType<Border>(),
            border => border.Name == "PART_BorderElement");
        await WaitForConditionAsync(
            () => javaArgumentInputBorder.Background is ISolidColorBrush brush &&
                  brush.Color == Color.Parse("#322525"),
            "Java argument modal error surface");
        viewModel.Settings.JavaArgumentsError = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(608, javaArgumentModal.ShoulderMaxWidth);
        Assert.True(javaModalShoulders.ZIndex > javaModalSheet.ZIndex);
        Assert.Same(javaModalShoulders, javaModalShoulderMask!.GetVisualParent());
        Assert.All(
            javaModalShoulders!.Children.OfType<Avalonia.Controls.Shapes.Path>(),
            shoulderPath => Assert.True(javaModalShoulderMask.ZIndex > shoulderPath.ZIndex));
        Assert.Equal(3, javaModalShoulderMask.Height);
        Assert.Equal(javaModalShoulders.Bounds.Width, javaModalShoulderMask.Bounds.Width);
        var javaArgumentModalPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_JAVA_ARGUMENT_MODAL_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(javaArgumentModalPreviewPath) && width == 1280)
            window.CaptureRenderedFrame()!.Save(javaArgumentModalPreviewPath, PngBitmapEncoderOptions.Default);
        viewModel.Settings.CancelAddJavaArgumentCommand.Execute(null);
        var maximumMemorySlider = javaView.FindControl<Slider>("JavaMaximumMemorySlider");
        var initialMemorySlider = javaView.FindControl<Slider>("JavaInitialMemorySlider");
        Assert.NotNull(maximumMemorySlider);
        Assert.NotNull(initialMemorySlider);
        Assert.Equal(1024, maximumMemorySlider!.Minimum);
        Assert.Equal(viewModel.Settings.MaximumJavaRamMb, maximumMemorySlider.Maximum);
        Assert.Equal(256, maximumMemorySlider.TickFrequency);
        Assert.True(maximumMemorySlider.IsSnapToTickEnabled);
        Assert.Equal(1024, initialMemorySlider!.Minimum);
        Assert.Equal(viewModel.Settings.JavaMaximumRamMb, initialMemorySlider.Maximum);
        Assert.Equal(256, initialMemorySlider.TickFrequency);
        Assert.True(initialMemorySlider.IsSnapToTickEnabled);
        Assert.True(viewModel.Settings.JavaInitialRamMb <= viewModel.Settings.JavaMaximumRamMb);
        viewModel.Settings.JavaArguments = "-Xms1G -Xmx8G -Dfile.encoding=UTF-8";
        Assert.Equal("-Dfile.encoding=UTF-8", viewModel.Settings.JavaArguments);
        Assert.True(viewModel.Settings.HasJavaArgumentsError);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "G1GC", StringComparison.OrdinalIgnoreCase));

        var javaSettingsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_JAVA_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(javaSettingsPreviewPath) && width == 1280)
        {
            var javaSettingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(javaSettingsFrame);
            javaSettingsFrame!.Save(javaSettingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(javaSettingsPreviewPath));
        }

        var visualCategory = viewModel.Settings.Categories.Single(category => category.Id == "visual");
        viewModel.Settings.SelectCategoryCommand.Execute(visualCategory);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            1,
            settingsView.GetVisualDescendants().OfType<Border>().Count(
                border => border.IsEffectivelyVisible && border.Classes.Contains("formGroup")));
        var visualCategoryHeadings = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsCategoryHeading"))
            .Select(text => text.Text)
            .ToArray();
        Assert.Single(visualCategoryHeadings);
        Assert.Equal(viewModel.Settings.VisualTitle, visualCategoryHeadings[0]);

        var generalCategory = viewModel.Settings.Categories.Single(category => category.Id == "general");
        viewModel.Settings.SelectCategoryCommand.Execute(generalCategory);
        Dispatcher.UIThread.RunJobs();

        var settingsPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(settingsPreviewPath) && width == 1280)
        {
            var settingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(settingsFrame);
            settingsFrame!.Save(settingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(settingsPreviewPath));
        }

        var compactSettingsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_SETTINGS_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactSettingsPreviewPath) && width == 1024)
        {
            var settingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(settingsFrame);
            settingsFrame!.Save(compactSettingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactSettingsPreviewPath));
        }

        var wideSettingsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_SETTINGS_WIDE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(wideSettingsPreviewPath) && width == 1920)
        {
            var settingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(settingsFrame);
            settingsFrame!.Save(wideSettingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(wideSettingsPreviewPath));
        }

        if (width == 1920)
        {
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactSettingsToolbar.IsVisible);
            Assert.False(settingsMain.IsHitTestVisible);
            Assert.True(Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X > 0);
            Assert.True(settingsRail!.IsHitTestVisible);

            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactSettingsToolbar.IsVisible);

            var generalCategoryButton = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("managerRailCategory") &&
                                  button.DataContext is SettingCategoryViewModel { Id: "general" });
            generalCategoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactSettingsToolbar.IsVisible);
            Assert.True(settingsMain.IsHitTestVisible);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X);
            Assert.True(compactSettingsTitle!.IsEffectivelyVisible);
            Assert.Equal(viewModel.Settings.ActiveCategoryTitle, compactSettingsTitle.Text);

            Assert.True(settingsView.TryCloseCompactContent());
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
        }

        if (compactSettingsLayout)
        {
            var settingsMainTranslation = Assert.IsType<TranslateTransform>(settingsMain.RenderTransform);
            var downloadsCategory = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("managerRailCategory") &&
                                  button.DataContext is SettingCategoryViewModel { Id: "downloads" });
            var categoryPoint = downloadsCategory.TranslatePoint(
                new Point(downloadsCategory.Bounds.Width / 2, downloadsCategory.Bounds.Height / 2),
                window);
            Assert.NotNull(categoryPoint);
            window.MouseDown(categoryPoint!.Value, MouseButton.Left);
            window.MouseUp(categoryPoint.Value, MouseButton.Left);
            await WaitForConditionAsync(
                () => settingsMain.IsHitTestVisible &&
                      Math.Abs(settingsMainTranslation.X) < 0.01 &&
                      compactSettingsTitle!.IsEffectivelyVisible,
                "compact settings content to open");
            Dispatcher.UIThread.RunJobs();
            Assert.True(settingsMain.IsHitTestVisible);
            Assert.InRange(Math.Abs(settingsMainTranslation.X), 0, 0.01);
            Assert.True(compactSettingsTitle!.IsEffectivelyVisible);
            Assert.Equal(viewModel.Settings.DownloadsTitle, compactSettingsTitle.Text);
            var compactTitleCenter = compactSettingsTitle.TranslatePoint(
                new Point(compactSettingsTitle.Bounds.Width / 2, compactSettingsTitle.Bounds.Height / 2),
                compactSettingsToolbar);
            Assert.NotNull(compactTitleCenter);
            Assert.InRange(
                Math.Abs(compactTitleCenter!.Value.X - (compactSettingsToolbar.Bounds.Width / 2)),
                0,
                1);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await WaitForConditionAsync(
                () => !settingsMain.IsHitTestVisible && settingsMainTranslation.X > 0,
                "compact settings content to close with Escape");
            Dispatcher.UIThread.RunJobs();
            Assert.False(settingsMain.IsHitTestVisible);
            Assert.True(settingsMainTranslation.X > 0);

            window.MouseDown(categoryPoint.Value, MouseButton.Left);
            window.MouseUp(categoryPoint.Value, MouseButton.Left);
            await WaitForConditionAsync(
                () => settingsMain.IsHitTestVisible && Math.Abs(settingsMainTranslation.X) < 0.01,
                "compact settings content to reopen");
            Dispatcher.UIThread.RunJobs();
            Assert.True(settingsMain.IsHitTestVisible);
            Assert.InRange(Math.Abs(settingsMainTranslation.X), 0, 0.01);

            var compactSettingsContentPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_SETTINGS_COMPACT_CONTENT_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactSettingsContentPreviewPath) && width == 1024)
            {
                var settingsContentFrame = window.CaptureRenderedFrame();
                Assert.NotNull(settingsContentFrame);
                settingsContentFrame!.Save(
                    compactSettingsContentPreviewPath,
                    PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactSettingsContentPreviewPath));
            }

            var settingsBack = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("detailBack"));
            var backPoint = settingsBack.TranslatePoint(
                new Point(settingsBack.Bounds.Width / 2, settingsBack.Bounds.Height / 2),
                window);
            Assert.NotNull(backPoint);
            window.MouseDown(backPoint!.Value, MouseButton.Left);
            window.MouseUp(backPoint.Value, MouseButton.Left);
            await WaitForConditionAsync(
                () => !settingsMain.IsHitTestVisible && settingsMainTranslation.X > 0,
                "compact settings content to close with the back button");
            Dispatcher.UIThread.RunJobs();
            Assert.False(settingsMain.IsHitTestVisible);
            Assert.True(settingsMainTranslation.X > 0);
        }

        foreach (var category in viewModel.Settings.Categories)
        {
            viewModel.Settings.SelectCategoryCommand.Execute(category);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(category.Id, viewModel.Settings.SelectedCategory);
            Assert.Same(category, Assert.Single(viewModel.Settings.Categories, item => item.IsSelected));
            Assert.Contains(
                settingsView.GetVisualDescendants().OfType<Border>(),
                border => border.IsEffectivelyVisible && border.Classes.Contains("formGroup"));
        }

        var aboutCategory = viewModel.Settings.Categories.Single(category => category.Id == "about");
        viewModel.Settings.SelectCategoryCommand.Execute(aboutCategory);
        await WaitForConditionAsync(
            () => !viewModel.Settings.IsAboutDataLoading,
            "About data to finish loading");
        Dispatcher.UIThread.RunJobs();
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Settings.AboutCurrentVersion));
        Assert.Equal(6, viewModel.Settings.AboutTeamMembers.Count);
        Assert.Contains(viewModel.Settings.AboutTeamMembers, member => member.GitHubLogin == "Aarav2709");
        Assert.DoesNotContain(viewModel.Settings.AboutTeamMembers, member => member.GitHubLogin == "CupRusk");
        Assert.All(viewModel.Settings.AboutTeamMembers, member => Assert.True(member.HasAvatar));
        Assert.InRange(viewModel.Settings.AboutContributors.Count, 1, 10);
        Assert.DoesNotContain(
            viewModel.Settings.AboutContributors,
            contributor => contributor.Login.Contains("bot", StringComparison.OrdinalIgnoreCase));
        var hiddenContributorCount = viewModel.Settings.HasMoreAboutContributors
            ? int.Parse(viewModel.Settings.AboutContributorOverflow[1..], CultureInfo.InvariantCulture)
            : 0;
        Assert.Equal(10, viewModel.Settings.AboutContributors.Count + hiddenContributorCount);
        if (width >= 1920)
        {
            Assert.Equal(10, viewModel.Settings.AboutContributors.Count);
            Assert.False(viewModel.Settings.HasMoreAboutContributors);
        }
        else
        {
            Assert.True(viewModel.Settings.HasMoreAboutContributors);
        }
        Assert.Equal("abcdef1", viewModel.Settings.AboutLatestCommitSha);
        Assert.Equal("feat: refine the native About page", viewModel.Settings.AboutLatestCommitHint);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<Border>(),
            border => border.IsEffectivelyVisible && border.Classes.Contains("aboutHero"));
        Assert.Equal(
            6,
            settingsView.GetVisualDescendants().OfType<Button>().Count(
                button => button.IsEffectivelyVisible && button.Classes.Contains("aboutTeamMember")));
        Assert.Equal(
            viewModel.Settings.AboutContributors.Count + (viewModel.Settings.HasMoreAboutContributors ? 1 : 0),
            settingsView.GetVisualDescendants().OfType<Button>().Count(
                button => button.IsEffectivelyVisible && button.Classes.Contains("aboutContributor")));
        var aboutView = Assert.Single(settingsView.GetVisualDescendants().OfType<SettingsAboutView>());
        var contributorsContainer = aboutView.FindControl<Border>("AboutContributorsContainer");
        var contributorsRow = aboutView.FindControl<StackPanel>("AboutContributorsRow");
        Assert.NotNull(contributorsContainer);
        Assert.NotNull(contributorsRow);
        var contributorsRowCenter = contributorsRow!.TranslatePoint(
            new Point(contributorsRow.Bounds.Width / 2, contributorsRow.Bounds.Height / 2),
            contributorsContainer);
        Assert.NotNull(contributorsRowCenter);
        Assert.InRange(
            Math.Abs(contributorsRowCenter!.Value.X - contributorsContainer!.Bounds.Width / 2),
            0,
            1);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == viewModel.Settings.AboutTeamHint);
        Assert.All(
            settingsView.GetVisualDescendants().OfType<Button>().Where(
                button => button.IsEffectivelyVisible &&
                          (button.Classes.Contains("aboutLink") ||
                           button.Classes.Contains("aboutTeamMember") ||
                           button.Classes.Contains("aboutContributor"))),
            AssertNoPressScale);
        Assert.All(
            settingsView.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Path>()
                .Where(path => path.IsEffectivelyVisible && path.Classes.Contains("aboutOpenIcon")),
            path => Assert.Contains(
                path.Transitions!,
                transition => transition is BrushTransition { Property: { } property } &&
                              property == Avalonia.Controls.Shapes.Shape.FillProperty));
        var aboutDisclaimer = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(textBlock => textBlock.IsEffectivelyVisible &&
                                 textBlock.Text == viewModel.Settings.AboutDisclaimer);
        Assert.IsType<StackPanel>(aboutDisclaimer.Parent);
        var technologyAttribution = aboutView.FindControl<StackPanel>("AboutTechnologyAttribution");
        var avaloniaButton = aboutView.FindControl<Button>("AboutAvaloniaButton");
        var dotNetButton = aboutView.FindControl<Button>("AboutDotNetButton");
        var avaloniaMark = aboutView.FindControl<Avalonia.Controls.Shapes.Path>("AboutAvaloniaMark");
        var dotNetBackground = aboutView.FindControl<Avalonia.Controls.Shapes.Path>("AboutDotNetBackground");
        var dotNetWordmark = aboutView.FindControl<Avalonia.Controls.Shapes.Path>("AboutDotNetWordmark");
        Assert.NotNull(technologyAttribution);
        Assert.True(technologyAttribution.IsEffectivelyVisible);
        Assert.NotNull(avaloniaButton);
        Assert.NotNull(dotNetButton);
        Assert.NotNull(avaloniaMark);
        Assert.NotNull(dotNetBackground);
        Assert.NotNull(dotNetWordmark);
        Assert.NotNull(avaloniaMark.Data);
        Assert.NotNull(dotNetBackground.Data);
        Assert.NotNull(dotNetWordmark.Data);
        Assert.True(avaloniaMark.Height >= 24);
        Assert.Contains(
            technologyAttribution.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == viewModel.Settings.AboutBuiltWithLabel);
        Assert.DoesNotContain(
            technologyAttribution.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text?.Contains("registered trademark", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Same(technologyAttribution, aboutDisclaimer.Parent);
        Assert.All(
            new[] { avaloniaButton, dotNetButton },
            button => Assert.Contains(
                button.Transitions!,
                transition => transition is TransformOperationsTransition));
        Assert.All(
            new[] { avaloniaMark, dotNetBackground, dotNetWordmark },
            path => Assert.Contains(
                path.Transitions!,
                transition => transition is BrushTransition { Property: { } property } &&
                              property == Avalonia.Controls.Shapes.Shape.FillProperty));

        viewModel.Settings.OpenDocumentationCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hyprismteam.github.io/Hyprism/docs/"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenLatestCommitCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://github.com/hyprismteam/Hyprism/commit/abcdef1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenAllContributorsCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://github.com/hyprismteam/Hyprism/graphs/contributors"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenHytaleEulaCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytale.com/eula"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenAvaloniaCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://avaloniaui.net/"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenDotNetCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://dotnet.microsoft.com/"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var aboutPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_ABOUT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(aboutPreviewPath) && width == 1280)
        {
            settingsScroll.ScrollToHome();
            Dispatcher.UIThread.RunJobs();
            var aboutFrame = window.CaptureRenderedFrame();
            Assert.NotNull(aboutFrame);
            aboutFrame!.Save(aboutPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(aboutPreviewPath));
        }

        var aboutTeamPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_ABOUT_TEAM_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(aboutTeamPreviewPath) && width == 1280)
        {
            settingsScroll.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            var aboutTeamFrame = window.CaptureRenderedFrame();
            Assert.NotNull(aboutTeamFrame);
            aboutTeamFrame!.Save(aboutTeamPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(aboutTeamPreviewPath));
        }

        var compactAboutPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_ABOUT_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactAboutPreviewPath) && width == 1024)
        {
            var aboutCategoryButton = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && ReferenceEquals(button.DataContext, aboutCategory));
            aboutCategoryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var compactAboutTranslation = Assert.IsType<TranslateTransform>(settingsMain.RenderTransform);
            await WaitForConditionAsync(
                () => settingsMain.IsHitTestVisible && Math.Abs(compactAboutTranslation.X) < 0.01,
                "compact About settings content to open");
            settingsScroll.ScrollToHome();
            Dispatcher.UIThread.RunJobs();
            var compactAboutFrame = window.CaptureRenderedFrame();
            Assert.NotNull(compactAboutFrame);
            compactAboutFrame!.Save(compactAboutPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactAboutPreviewPath));
        }

        window.Close();
    }

    private static void AssertNoPressScale(Control control)
    {
        if (control.RenderTransform is ScaleTransform scale)
        {
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, scale.ScaleY);
        }
    }

    private static async Task WaitForAvaloniaPropertyAsync(
        AvaloniaObject source,
        AvaloniaProperty property,
        Func<bool> condition,
        string description)
        => await AvaloniaTestWait.PropertyAsync(source, property, condition, description);

    private static async Task WaitForConditionAsync(Func<bool> condition, string description)
        => await AvaloniaTestWait.UntilAsync(condition, description);

    private static T? FindVisualByName<T>(Visual root, string name)
        where T : Control
        => root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal));

    private static void AssertUsesApplicationScrollBar(ScrollViewer scrollViewer)
    {
        Assert.IsType<SmoothScrollViewer>(scrollViewer);
        var contentPresenter = scrollViewer.GetVisualDescendants()
            .OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>()
            .Single(control => control.Name == "PART_ContentPresenter");
        Assert.Equal(1, Grid.GetColumnSpan(contentPresenter));

        var scrollBar = scrollViewer.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(control => control.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.Equal(12, scrollBar.Width);
        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(scrollBar.Background).Color.A);

        var thumbs = scrollBar.GetVisualDescendants().OfType<Thumb>().ToArray();
        if (thumbs.Length == 0)
        {
            Assert.False(scrollBar.IsVisible);
            return;
        }

        var thumb = Assert.Single(thumbs);
        var scrollTrack = thumb.GetVisualAncestors().OfType<Track>().Single();
        Assert.True(scrollTrack.IsDirectionReversed);
        Assert.Equal(6, thumb.Width);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, thumb.HorizontalAlignment);
        Assert.True(thumb.CornerRadius.TopLeft >= 999);
        Assert.Contains(
            thumb.Transitions!,
            transition => transition is TransformOperationsTransition { Property.Name: "RenderTransform" });

        var thumbBorder = thumb.GetVisualDescendants().OfType<Border>().Single();
        Assert.True(thumbBorder.CornerRadius.TopLeft >= 999);
        Assert.Contains(
            thumbBorder.Transitions!,
            transition => transition is BrushTransition { Property.Name: "Background" });

        var track = scrollBar.GetVisualDescendants()
            .OfType<Avalonia.Controls.Shapes.Rectangle>()
            .Single(rectangle => rectangle.Name == "TrackRect");
        Assert.Equal(0, track.Opacity);

        var arrowButtons = scrollBar.GetVisualDescendants()
            .OfType<RepeatButton>()
            .Where(button => button.Name is "PART_LineUpButton" or "PART_LineDownButton")
            .ToArray();
        Assert.Equal(2, arrowButtons.Length);
        Assert.All(arrowButtons, button => Assert.False(button.IsVisible));
    }

    private sealed class CultureRestoreScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }

    private sealed class TinyPngHandler : HttpMessageHandler
    {
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public static byte[] ImageBytes => Png;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Png),
                RequestMessage = request
            });
    }
}
