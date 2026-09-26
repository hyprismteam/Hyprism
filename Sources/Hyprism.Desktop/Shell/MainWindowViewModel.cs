// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Desktop.Integrations.GitHub;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Screens.Profiles;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Game.Sources;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Desktop.Shell;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable, IStartupLoadingState
{
    private const string InstancesPage = "instances";
    private const string NewsPage = "news";
    private const string ProfilesPage = "profiles";
    private const string SettingsPage = "settings";

    private readonly IDesktopSettingsStore _settingsStore;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly IFilePicker? _filePicker;
    private readonly IGitHubClient? _gitHubClient;
    private readonly IMirrorCatalog? _mirrorCatalog;
    private readonly IMirrorDiscovery? _mirrorDiscovery;
    private readonly IGameVersionCatalog? _versionCatalog;
    private readonly IGameProcessTracker _gameProcess;
    private readonly IInstanceRepository _instances;
    private readonly IGpuProvider? _gpuProvider;
    private readonly HttpClient _httpClient;
    private readonly StringLocalizer _localizer;
    private bool _isOfficialProfile;

    [ObservableProperty]
    private bool _isStartupLoading;

    [ObservableProperty]
    private string _startupLoadingStatus = string.Empty;

    [ObservableProperty]
    private string _currentPage = InstancesPage;

    [ObservableProperty]
    private string _currentPageTitle = string.Empty;

    [ObservableProperty]
    private string _userName = "Hyprism";

    [ObservableProperty]
    private string _userInitial = "H";

    [ObservableProperty]
    private string _accountType = "Offline Account";

    public MainWindowViewModel(
        IInstanceRepository instances,
        IProfileManager profiles,
        IProfileRepository profileRepository,
        IGameLaunchCoordinator gameLaunchCoordinator,
        IGameInstallationWorkflow installationWorkflow,
        IGameProcessTracker gameProcess,
        IProgressReporter progress,
        IDesktopSettingsStore settingsStore,
        IHytaleNewsClient newsClient,
        IExternalUriLauncher uriLauncher,
        HttpClient httpClient,
        StringLocalizer localizer,
        IFilePicker? filePicker = null,
        IGitHubClient? gitHubClient = null,
        IMirrorCatalog? mirrorCatalog = null,
        IMirrorDiscovery? mirrorDiscovery = null,
        IGameVersionCatalog? versionCatalog = null,
        IModManager? modManager = null,
        IHytaleAuthenticator? authenticator = null,
        RemoteImageCache? remoteImageCache = null,
        IGameConsoleService? gameConsole = null,
        IGpuProvider? gpuProvider = null,
        LogSessionPaths? logSession = null)
    {
        _instances = instances;
        _settingsStore = settingsStore;
        _uriLauncher = uriLauncher;
        _filePicker = filePicker;
        _gitHubClient = gitHubClient;
        _versionCatalog = versionCatalog;
        _mirrorDiscovery = mirrorDiscovery;
        _mirrorCatalog = mirrorCatalog;
        _gameProcess = gameProcess;
        _gpuProvider = gpuProvider;
        _httpClient = httpClient;
        _localizer = localizer;
        _isOfficialProfile = profileRepository.GetSelectedProfile()?.IsOfficial == true;

        Settings = CreateSettingsViewModel(profileRepository);
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        Profiles = new ProfilesViewModel(
            profiles,
            profileRepository,
            _uriLauncher,
            _localizer,
            authenticator,
            _instances);
        Profiles.ActiveProfileChanged += OnActiveProfileChanged;
        Profiles.PropertyChanged += OnProfilesPropertyChanged;

        News = new NewsViewModel(newsClient, _uriLauncher, _httpClient, _localizer, remoteImageCache);
        News.PropertyChanged += OnNewsPropertyChanged;
        Instances = new InstancesViewModel(
            instances,
            gameLaunchCoordinator,
            installationWorkflow,
            gameProcess,
            progress,
            _uriLauncher,
            _httpClient,
            _localizer,
            filePicker,
            versionCatalog,
            modManager,
            remoteImageCache,
            gameConsole,
            logSession);
        Instances.PropertyChanged += OnInstancesPropertyChanged;

        UserName = profiles.GetNick();
        UserInitial = string.IsNullOrWhiteSpace(UserName)
            ? "H"
            : UserName[..1].ToUpperInvariant();
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];

        _localizer.LanguageChanged += ApplyLanguage;
        CurrentPageTitle = InstancesLabel;
    }

    public SettingsViewModel Settings { get; }
    public ProfilesViewModel Profiles { get; }
    public NewsViewModel News { get; }
    public InstancesViewModel Instances { get; }

    public Bitmap? ActiveProfileAvatar => Profiles.ActiveProfile?.Avatar;
    public bool HasActiveProfileAvatar => ActiveProfileAvatar is not null;

    public string InstancesLabel => _localizer["dock.instances"];
    public string NewsLabel => _localizer["dock.news"];
    public string ProfilesLabel => _localizer["dock.profiles"];
    public string SettingsLabel => _localizer["dock.settings"];
    public string StartupLoadingTitle => _localizer["startup.loading.title"];
    public string LauncherVersion => DesktopApplicationInfo.Version;

    public bool IsInstances => CurrentPage == InstancesPage;
    public bool IsNews => CurrentPage == NewsPage;
    public bool IsProfiles => CurrentPage == ProfilesPage;
    public bool IsSettings => CurrentPage == SettingsPage;
    public bool IsPlaceholderPage => !IsInstances && !IsNews && !IsProfiles && !IsSettings;

    public bool IsBottomSheetMounted =>
        Instances.IsModCatalogPreviewMounted ||
        Instances.HasModCatalogInstallConfirmation ||
        Instances.IsInstanceEditMounted ||
        Instances.HasPendingManagedInstanceDeletion ||
        Settings.IsAddingJavaArgument ||
        Settings.IsAddingEnvironmentVariable ||
        Settings.IsAddingAuthServer ||
        Profiles.IsProfileEditMounted ||
        Profiles.HasPendingProfileDeletion;

    [RelayCommand]
    private void Navigate(string? page)
    {
        CurrentPage = page switch
        {
            InstancesPage => InstancesPage,
            NewsPage => NewsPage,
            ProfilesPage => ProfilesPage,
            SettingsPage => SettingsPage,
            _ => InstancesPage
        };

        CurrentPageTitle = CurrentPage switch
        {
            InstancesPage => InstancesLabel,
            NewsPage => NewsLabel,
            ProfilesPage => ProfilesLabel,
            SettingsPage => SettingsLabel,
            _ => InstancesLabel
        };

        NotifyPageStateChanged();

        if (IsNews)
        {
            if (News.HasLoadedNews)
                News.RestartImageLoading();
            else
                _ = News.LoadAsync();
        }
    }

    public void BeginStartupLoading()
    {
        StartupLoadingStatus = _localizer["startup.loading.content"];
        IsStartupLoading = true;
    }

    public async Task PreloadStartupDataAsync(CancellationToken cancellationToken)
    {
        StartupLoadingStatus = _localizer["startup.loading.content"];
        await Task.WhenAll(
            News.LoadAsync(waitForImages: true, cancellationToken: cancellationToken),
            Settings.PreloadAboutDataAsync(cancellationToken),
            Settings.PreloadStorageUsageAsync(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        StartupLoadingStatus = _localizer["startup.loading.ready"];
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    public void CompleteStartupLoading()
        => IsStartupLoading = false;

    private SettingsViewModel CreateSettingsViewModel(IProfileRepository profileRepository)
        => new(
            _settingsStore,
            _uriLauncher,
            _localizer,
            _filePicker,
            _gitHubClient,
            _mirrorCatalog,
            _mirrorDiscovery,
            _versionCatalog,
            _gameProcess,
            _instances,
            _gpuProvider,
            profileRepository,
            _httpClient);

    private void ApplyLanguage(string language)
    {
        Settings.RefreshLocalization();
        Profiles.RefreshLocalization();
        News.RefreshLocalization();
        Instances.RefreshLocalization();
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];
        CurrentPageTitle = CurrentPage switch
        {
            InstancesPage => InstancesLabel,
            NewsPage => NewsLabel,
            ProfilesPage => ProfilesLabel,
            SettingsPage => SettingsLabel,
            _ => InstancesLabel
        };
        OnPropertyChanged(string.Empty);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.IsAddingJavaArgument) or
            nameof(SettingsViewModel.IsAddingEnvironmentVariable) or
            nameof(SettingsViewModel.IsAddingAuthServer))
            OnPropertyChanged(nameof(IsBottomSheetMounted));
    }

    private void OnInstancesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(InstancesViewModel.IsModCatalogPreviewMounted) or
            nameof(InstancesViewModel.HasModCatalogInstallConfirmation) or
            nameof(InstancesViewModel.IsInstanceEditMounted) or
            nameof(InstancesViewModel.HasPendingManagedInstanceDeletion) or
            nameof(InstancesViewModel.IsBottomSheetMounted))
            OnPropertyChanged(nameof(IsBottomSheetMounted));
    }

    private void OnNewsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(e.PropertyName);

    private void OnActiveProfileChanged(object? sender, ActiveProfileChangedEventArgs e)
    {
        UserName = e.Name;
        UserInitial = string.IsNullOrWhiteSpace(UserName)
            ? "H"
            : UserName[..1].ToUpperInvariant();
        _isOfficialProfile = e.IsOfficial;
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];
    }

    private void OnProfilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfilesViewModel.IsProfileEditMounted) or
            nameof(ProfilesViewModel.HasPendingProfileDeletion))
            OnPropertyChanged(nameof(IsBottomSheetMounted));

        if (e.PropertyName is nameof(ProfilesViewModel.ActiveProfile) or null)
        {
            OnPropertyChanged(nameof(ActiveProfileAvatar));
            OnPropertyChanged(nameof(HasActiveProfileAvatar));
        }
    }

    private void NotifyPageStateChanged()
    {
        OnPropertyChanged(nameof(IsInstances));
        OnPropertyChanged(nameof(IsNews));
        OnPropertyChanged(nameof(IsProfiles));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsPlaceholderPage));
        OnPropertyChanged(nameof(IsNewsLandingVisible));
        OnPropertyChanged(nameof(IsNewsArticleVisible));
        OnPropertyChanged(nameof(IsNewsArticleStatusVisible));
        OnPropertyChanged(nameof(IsNewsFeedVisible));
        OnPropertyChanged(nameof(IsNewsArticleContext));
        OnPropertyChanged(nameof(IsNewsArticleEmpty));
        OnPropertyChanged(nameof(CompactNewsPageIndex));
    }

    public void Dispose()
    {
        _localizer.LanguageChanged -= ApplyLanguage;
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Profiles.ActiveProfileChanged -= OnActiveProfileChanged;
        Profiles.PropertyChanged -= OnProfilesPropertyChanged;
        Instances.PropertyChanged -= OnInstancesPropertyChanged;
        News.PropertyChanged -= OnNewsPropertyChanged;
        Instances.Dispose();
        News.Dispose();
        Profiles.Dispose();
        Settings.Dispose();
    }
}
