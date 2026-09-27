// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Accounts;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Models;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Screens.Profiles;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Shell;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class OnboardingViewModelTests
{
    [AvaloniaFact]
    public async Task WizardUsesSharedPickersAndSkipsWarningWhenMirrorExists()
    {
        var settingsStore = new Mock<IDesktopSettingsStore>();
        settingsStore.SetupProperty(store => store.Language, "en-US");
        var versions = new Mock<IGameVersionCatalog>();
        versions.SetupGet(catalog => catalog.EnabledMirrorCount).Returns(1);
        var profilesRepository = new Mock<IProfileRepository>();
        profilesRepository.Setup(repository => repository.GetProfiles()).Returns([]);
        var localizer = new StringLocalizer("en-US");
        var uriLauncher = new Mock<IExternalUriLauncher>();
        using var settings = new SettingsViewModel(
            settingsStore.Object, uriLauncher.Object, localizer);
        using var profiles = new ProfilesViewModel(
            new Mock<IProfileManager>().Object,
            profilesRepository.Object,
            uriLauncher.Object,
            localizer);
        using var onboarding = new OnboardingViewModel(
            settingsStore.Object, settings, profiles, versions.Object, localizer, () => { });
        var view = new OnboardingView { DataContext = onboarding };
        var window = new Window { Width = 1280, Height = 800, Content = view };
        window.Show();

        async Task ClickAndWaitAsync(string name, OnboardingStep expectedStep, string contentName)
        {
            view.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var content = view.FindControl<StackPanel>(contentName)!;
            await AvaloniaTestWait.UntilAsync(
                () => onboarding.Step == expectedStep && content.IsHitTestVisible,
                $"{expectedStep} step to finish opening");
        }

        await ClickAndWaitAsync("WelcomeNextButton", OnboardingStep.Language, "LanguageContent");
        Assert.Equal(OnboardingStep.Language, onboarding.Step);
        var languagePicker = view.GetVisualDescendants().OfType<SettingsLanguagePickerView>().Single();
        Assert.True(languagePicker.IsEffectivelyVisible);
        Assert.False(languagePicker.FindControl<TextBlock>("LanguagePickerHeading")!.IsEffectivelyVisible);

        await ClickAndWaitAsync("LanguageNextButton", OnboardingStep.Appearance, "AppearanceContent");
        Assert.Equal(OnboardingStep.Appearance, onboarding.Step);
        var appearancePicker = view.GetVisualDescendants().OfType<SettingsAppearancePickerView>().Single();
        Assert.True(appearancePicker.IsEffectivelyVisible);
        Assert.True(appearancePicker.FindControl<Border>("AppearancePickerDivider")!.IsEffectivelyVisible);
        Assert.DoesNotContain(appearancePicker.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Classes.Contains("settingsCategoryHeading") && text.IsEffectivelyVisible);

        await ClickAndWaitAsync("AppearanceNextButton", OnboardingStep.Account, "AccountContent");
        Assert.Equal(OnboardingStep.Account, onboarding.Step);
        await ClickAndWaitAsync("ChooseOfflineButton", OnboardingStep.OfflineName, "OfflineNameContent");
        Assert.Equal(OnboardingStep.OfflineName, onboarding.Step);
        Assert.False(view.FindControl<StackPanel>("WarningContent")!.IsEffectivelyVisible);
        onboarding.Step = OnboardingStep.Finishing;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var steps = view.GetVisualDescendants().OfType<StackPanel>()
            .Single(panel => panel.Classes.Contains("onboardingSteps"));
        var logo = view.FindControl<Border>("OnboardingLogoAnchor")!;
        Assert.InRange(Math.Abs(steps.Bounds.Y + logo.Bounds.Y + logo.Bounds.Height / 2 -
                                window.Bounds.Height / 2), 0, 3);
        window.Close();
    }

    [AvaloniaFact]
    public void ExistingLauncherDataDoesNotReopenOnboarding()
    {
        using var httpClient = new HttpClient();
        using var launcher = MainWindowViewModelFactory.Create(httpClient);

        launcher.BeginOnboardingIfNeeded();

        Assert.Null(launcher.Onboarding);
    }

    [AvaloniaTheory]
    [InlineData(0, OnboardingStep.DownloadWarning)]
    [InlineData(1, OnboardingStep.OfflineName)]
    public void OfflineChoiceShowsWarningOnlyWithoutEnabledMirror(
        int mirrorCount,
        OnboardingStep expectedStep)
    {
        var settingsStore = new Mock<IDesktopSettingsStore>();
        var versions = new Mock<IGameVersionCatalog>();
        versions.SetupGet(catalog => catalog.EnabledMirrorCount).Returns(mirrorCount);
        var profilesRepository = new Mock<IProfileRepository>();
        profilesRepository.Setup(repository => repository.GetProfiles()).Returns([]);
        var localizer = new StringLocalizer("en-US");
        var uriLauncher = new Mock<IExternalUriLauncher>();
        using var settings = new SettingsViewModel(
            settingsStore.Object, uriLauncher.Object, localizer);
        using var profiles = new ProfilesViewModel(
            new Mock<IProfileManager>().Object,
            profilesRepository.Object,
            uriLauncher.Object,
            localizer);
        using var onboarding = new OnboardingViewModel(
            settingsStore.Object, settings, profiles, versions.Object, localizer, () => { });

        Assert.Equal(OnboardingStep.Welcome, onboarding.Step);
        onboarding.NextCommand.Execute(null);
        Assert.Equal(OnboardingStep.Language, onboarding.Step);
        onboarding.NextCommand.Execute(null);
        Assert.Equal(OnboardingStep.Appearance, onboarding.Step);
        onboarding.NextCommand.Execute(null);
        Assert.Equal(OnboardingStep.Account, onboarding.Step);

        onboarding.ChooseOfflineCommand.Execute(null);
        Assert.Equal(expectedStep, onboarding.Step);
        settingsStore.VerifySet(store => store.HasCompletedOnboarding = true, Times.Never);

        var window = new Window
        {
            Width = 1024,
            Height = 700,
            Content = new OnboardingView { DataContext = onboarding }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var warningTitle = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Text == onboarding.WarningTitle);
        Assert.Equal(mirrorCount == 0, warningTitle.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void CompletionIsSavedOnlyAfterOfflineProfileCreation()
    {
        var savedProfiles = new List<Profile>();
        var selectedId = string.Empty;
        var settingsStore = new Mock<IDesktopSettingsStore>();
        settingsStore.SetupProperty(store => store.HasCompletedOnboarding);
        var profileRepository = new Mock<IProfileRepository>();
        profileRepository.Setup(repository => repository.GetProfiles())
            .Returns(() => [.. savedProfiles]);
        profileRepository.Setup(repository => repository.GetSelectedProfileId())
            .Returns(() => selectedId);
        profileRepository.Setup(repository => repository.CreateProfile(
                It.IsAny<string>(), It.IsAny<string>(), false))
            .Returns((string name, string uuid, bool _) =>
            {
                var profile = new Profile
                {
                    Id = "offline-1",
                    Name = name,
                    UUID = uuid,
                    IsOfficial = false
                };
                savedProfiles.Add(profile);
                return profile;
            });
        profileRepository.Setup(repository => repository.SwitchProfile(It.IsAny<string>()))
            .Returns((string id) =>
            {
                selectedId = id;
                return true;
            });
        var localizer = new StringLocalizer("en-US");
        var uriLauncher = new Mock<IExternalUriLauncher>();
        using var settings = new SettingsViewModel(
            settingsStore.Object, uriLauncher.Object, localizer);
        using var profiles = new ProfilesViewModel(
            new Mock<IProfileManager>().Object,
            profileRepository.Object,
            uriLauncher.Object,
            localizer);
        var completions = 0;
        using var onboarding = new OnboardingViewModel(
            settingsStore.Object, settings, profiles, null, localizer, () => completions++);

        onboarding.ChooseOfflineCommand.Execute(null);
        Assert.Equal(OnboardingStep.DownloadWarning, onboarding.Step);
        Assert.False(settingsStore.Object.HasCompletedOnboarding);
        onboarding.ContinueOfflineCommand.Execute(null);
        profiles.OfflineProfileName = "Player_123";
        onboarding.CreateOfflineCommand.Execute(null);

        Assert.True(settingsStore.Object.HasCompletedOnboarding);
        Assert.Equal(1, completions);
        Assert.Equal("Player_123", profiles.ActiveProfile?.Name);
    }
}
