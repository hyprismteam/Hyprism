// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Core.Game.Versions;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Screens.Profiles;
using Hyprism.Desktop.Screens.Settings;

namespace Hyprism.Desktop.Shell;

public enum OnboardingStep
{
    Welcome,
    Language,
    Appearance,
    Account,
    Official,
    DownloadWarning,
    OfflineName,
    Finishing
}

/// <summary>Coordinates first-run setup using the existing settings and profile flows.</summary>
public sealed partial class OnboardingViewModel : ObservableObject, IDisposable
{
    private readonly IDesktopSettingsStore _settings;
    private readonly IGameVersionCatalog? _versions;
    private readonly StringLocalizer _localizer;
    private readonly Action _completed;
    private bool _offlineWarningShown;
    private bool _isCompleting;

    public event Action? CompletionRequested;

    [ObservableProperty] private OnboardingStep _step;

    public OnboardingViewModel(
        IDesktopSettingsStore settings,
        SettingsViewModel settingsViewModel,
        ProfilesViewModel profiles,
        IGameVersionCatalog? versions,
        StringLocalizer localizer,
        Action completed)
    {
        _settings = settings;
        _versions = versions;
        _localizer = localizer;
        _completed = completed;
        Settings = settingsViewModel;
        Profiles = profiles;
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    public SettingsViewModel Settings { get; }
    public ProfilesViewModel Profiles { get; }

    public bool IsWelcome => Step == OnboardingStep.Welcome;
    public bool IsLanguage => Step == OnboardingStep.Language;
    public bool IsAppearance => Step == OnboardingStep.Appearance;
    public bool IsAccount => Step == OnboardingStep.Account;
    public bool IsOfficial => Step == OnboardingStep.Official;
    public bool IsDownloadWarning => Step == OnboardingStep.DownloadWarning;
    public bool IsOfflineName => Step == OnboardingStep.OfflineName;
    public bool IsFinishing => Step == OnboardingStep.Finishing;
    public bool CanGoBack => Step is not (OnboardingStep.Welcome or OnboardingStep.Finishing);
    public bool ShouldShowDownloadWarning => _versions is null || _versions.EnabledMirrorCount <= 0;
    public OnboardingStep PreviousStep => Step switch
    {
        OnboardingStep.Language => OnboardingStep.Welcome,
        OnboardingStep.Appearance => OnboardingStep.Language,
        OnboardingStep.Account => OnboardingStep.Appearance,
        OnboardingStep.Official or OnboardingStep.DownloadWarning => OnboardingStep.Account,
        OnboardingStep.OfflineName => _offlineWarningShown
            ? OnboardingStep.DownloadWarning
            : OnboardingStep.Account,
        _ => OnboardingStep.Welcome
    };

    public string WelcomeTitle => _localizer["onboarding.welcome"];
    public string WelcomeDescription => _localizer["onboarding.letsSetUp"];
    public string LanguageTitle => _localizer["onboarding.chooseLanguage"];
    public string LanguageDescription => _localizer["onboarding.selectLanguageHint"];
    public string AppearanceTitle => _localizer["onboarding.customizeAppearance"];
    public string AppearanceDescription => _localizer["onboarding.appearanceDescription"];
    public string AccountTitle => _localizer["onboarding.accountChoice.title"];
    public string AccountDescription => _localizer["profiles.wizard.chooseType"];
    public string OfficialTitle => _localizer["profiles.wizard.authTitle"];
    public string OfficialDescription => _localizer["profiles.wizard.authDesc"];
    public string WarningTitle => _localizer["onboarding.warning.title"];
    public string WarningDescription => _localizer["onboarding.warning.description"];
    public string WarningAction => _localizer["onboarding.warning.noSourcesHint"];
    public string NameTitle => _localizer["profiles.wizard.nameTitle"];
    public string NameDescription => _localizer["profiles.wizard.nameDesc"];
    public string OfficialLabel => _localizer["profiles.wizard.official"];
    public string OfficialHint => _localizer["profiles.wizard.officialDesc"];
    public string OfflineLabel => _localizer["profiles.wizard.unofficial"];
    public string OfflineHint => _localizer["profiles.wizard.unofficialDesc"];
    public string BackLabel => _localizer["common.back"];
    public string ContinueLabel => _localizer["common.continue"];
    public string CreateLabel => _localizer["profiles.wizard.create"];

    partial void OnStepChanged(OnboardingStep value)
    {
        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsLanguage));
        OnPropertyChanged(nameof(IsAppearance));
        OnPropertyChanged(nameof(IsAccount));
        OnPropertyChanged(nameof(IsOfficial));
        OnPropertyChanged(nameof(IsDownloadWarning));
        OnPropertyChanged(nameof(IsOfflineName));
        OnPropertyChanged(nameof(IsFinishing));
        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    private void Next()
    {
        Step = Step switch
        {
            OnboardingStep.Welcome => OnboardingStep.Language,
            OnboardingStep.Language => OnboardingStep.Appearance,
            OnboardingStep.Appearance => OnboardingStep.Account,
            _ => Step
        };
    }

    [RelayCommand]
    private void Back()
    {
        if (Step is OnboardingStep.Official or OnboardingStep.OfflineName)
            Profiles.CancelCreationCommand.Execute(null);

        Step = PreviousStep;
    }

    [RelayCommand]
    private void ChooseOfficial()
    {
        Profiles.CancelCreationCommand.Execute(null);
        Profiles.BeginOfficialCreationCommand.Execute(null);
        Step = OnboardingStep.Official;
    }

    [RelayCommand]
    private void ChooseOffline()
    {
        Profiles.CancelCreationCommand.Execute(null);
        Profiles.BeginOfflineCreationCommand.Execute(null);
        _offlineWarningShown = ShouldShowDownloadWarning;
        Step = _offlineWarningShown ? OnboardingStep.DownloadWarning : OnboardingStep.OfflineName;
    }

    [RelayCommand]
    private void ContinueOffline() => Step = OnboardingStep.OfflineName;

    [RelayCommand]
    private void CreateOffline()
    {
        if (!Profiles.CanCreateOfflineProfile)
            return;

        Profiles.CreateOfflineProfileCommand.Execute(null);
        if (Profiles.ActiveProfile is { IsOfficial: false })
            Complete();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SignInAsync()
    {
        await Profiles.SignInWithHytaleCommand.ExecuteAsync(null);
        if (Profiles.ActiveProfile is { IsOfficial: true })
            Complete();
    }

    private void Complete()
    {
        if (_isCompleting)
            return;

        _isCompleting = true;
        _settings.HasCompletedOnboarding = true;
        if (CompletionRequested is { } requested)
            requested();
        else
            FinishCompletion();
    }

    public void FinishCompletion() => _completed();

    private void OnLanguageChanged(string language)
    {
        OnPropertyChanged(nameof(WelcomeTitle));
        OnPropertyChanged(nameof(WelcomeDescription));
        OnPropertyChanged(nameof(LanguageTitle));
        OnPropertyChanged(nameof(LanguageDescription));
        OnPropertyChanged(nameof(AppearanceTitle));
        OnPropertyChanged(nameof(AppearanceDescription));
        OnPropertyChanged(nameof(AccountTitle));
        OnPropertyChanged(nameof(AccountDescription));
        OnPropertyChanged(nameof(OfficialTitle));
        OnPropertyChanged(nameof(OfficialDescription));
        OnPropertyChanged(nameof(WarningTitle));
        OnPropertyChanged(nameof(WarningDescription));
        OnPropertyChanged(nameof(WarningAction));
        OnPropertyChanged(nameof(NameTitle));
        OnPropertyChanged(nameof(NameDescription));
        OnPropertyChanged(nameof(OfficialLabel));
        OnPropertyChanged(nameof(OfficialHint));
        OnPropertyChanged(nameof(OfflineLabel));
        OnPropertyChanged(nameof(OfflineHint));
        OnPropertyChanged(nameof(BackLabel));
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(CreateLabel));
    }

    public void Dispose() => _localizer.LanguageChanged -= OnLanguageChanged;
}
