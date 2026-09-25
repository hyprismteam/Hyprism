// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Integrations.GitHub;
using Hyprism.Desktop.Platform;
using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Authentication;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Sources;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Models;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private const int MinimumJavaMemoryMb = 1024;
    private const int JavaMemoryStepMb = 256;
    private const string DefaultAuthServer = "sessions.sanasol.ws";
    private static readonly TimeSpan MinimumInstanceFolderActionDuration = TimeSpan.FromMilliseconds(450);
    private static readonly HashSet<string> BotLogins = new(StringComparer.OrdinalIgnoreCase)
    {
        "copilot",
        "github-actions",
        "dependabot",
        "renovate",
        "semantic-release-bot",
        "allcontributors",
        "imgbot",
        "codecov",
        "snyk-bot",
        "greenkeeper",
        "google-labs-jules"
    };

    private readonly IDesktopSettingsStore _settings;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly StringLocalizer _localizer;
    private readonly IFilePicker? _filePicker;
    private readonly IGitHubClient? _gitHubClient;
    private readonly IMirrorCatalog? _mirrorCatalog;
    private readonly IMirrorDiscovery? _mirrorDiscovery;
    private readonly IGameVersionCatalog? _versionCatalog;
    private readonly IGameProcessTracker? _gameProcess;
    private readonly IInstanceRepository? _instanceRepository;
    private readonly IProfileRepository? _profileRepository;
    private readonly HttpClient? _httpClient;
    private bool _updatingJavaMemory;
    private bool _updatingJavaArguments;
    private bool _aboutDataLoadStarted;
    private bool _aboutDataLoaded;
    private bool _downloadSourcesProbeStarted;
    private bool _authServerProbeStarted;
    private bool _storageUsageLoadStarted;
    private bool _disposed;
    private CancellationTokenSource? _sourceProbeCancellation;
    private CancellationTokenSource? _authServerProbeCancellation;
    private CancellationTokenSource? _authServerAddCancellation;
    private bool _isAuthServerCancellationArmed;
    private CancellationTokenSource? _storageUsageCancellation;
    private CancellationTokenSource? _instanceFolderChangeCancellation;
    private bool _isInstanceFolderChangeCancellationArmed;
    private bool _canCancelInstanceFolderChange;
    private Task _storageUsageLoadTask = Task.CompletedTask;
    private int _aboutContributorSlotCapacity = 9;
    private GitHubCommit? _latestMainCommit;
    private LauncherStorageUsage? _latestStorageUsage;
    private readonly List<AboutContributorViewModel> _aboutContributorPool = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneral))]
    [NotifyPropertyChangedFor(nameof(IsDownloads))]
    [NotifyPropertyChangedFor(nameof(IsJava))]
    [NotifyPropertyChangedFor(nameof(IsVisual))]
    [NotifyPropertyChangedFor(nameof(IsNetwork))]
    [NotifyPropertyChangedFor(nameof(IsData))]
    [NotifyPropertyChangedFor(nameof(IsAbout))]
    [NotifyPropertyChangedFor(nameof(ActiveCategoryTitle))]
    private string _selectedCategory = "general";

    [ObservableProperty] private SettingChoiceViewModel _selectedLanguage;
    [ObservableProperty] private SettingChoiceViewModel _selectedGpuPreference;
    private readonly IReadOnlyList<GpuAdapterInfo> _detectedGpuAdapters = [];
    [ObservableProperty] private bool _closeAfterLaunch;
    [ObservableProperty] private bool _showAlphaMods;
    [ObservableProperty] private bool _musicEnabled;
    [ObservableProperty] private bool _disableNews;
    [ObservableProperty] private bool _showDiscordAnnouncements;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthServerVisible))]
    private bool _onlineMode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseBundledJava))]
    private bool _useCustomJava;
    [ObservableProperty] private string _authDomain;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAuthServerCommand))]
    private string _newAuthServer = string.Empty;
    [ObservableProperty] private bool _isAddingAuthServer;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthServerCancellationArmed))]
    [NotifyCanExecuteChangedFor(nameof(AddAuthServerCommand))]
    private bool _isCheckingAuthServer;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthServerAddStatus))]
    private string _authServerAddStatus = string.Empty;
    [ObservableProperty] private bool _isAuthServerAddError;
    [ObservableProperty] private string _customJavaPath;
    [ObservableProperty] private string _javaArguments;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddJavaArgumentCommand))]
    private string _newJavaArgument = string.Empty;
    [ObservableProperty] private bool _isAddingJavaArgument;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaMaximumRamValue))]
    [NotifyPropertyChangedFor(nameof(JavaInitialRamMaximum))]
    private double _javaMaximumRamMb;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaInitialRamValue))]
    private double _javaInitialRamMb;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJavaArgumentsError))]
    private string _javaArgumentsError = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddEnvironmentVariableCommand))]
    private string _newEnvironmentVariable = string.Empty;
    [ObservableProperty] private bool _isAddingEnvironmentVariable;
    [ObservableProperty] private bool _isBrowsingCustomJava;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnvironmentVariablesError))]
    private string _environmentVariablesError = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isCompactLayout;
    [ObservableProperty] private bool _isAboutDataLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeInstanceFolder))]
    [NotifyPropertyChangedFor(nameof(CanResetInstanceFolder))]
    [NotifyPropertyChangedFor(nameof(CanUseInstanceFolderChangeAction))]
    private bool _isGameRunning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeInstanceFolder))]
    [NotifyPropertyChangedFor(nameof(CanResetInstanceFolder))]
    [NotifyPropertyChangedFor(nameof(IsInstanceFolderChangeCancellationArmed))]
    private bool _isChangingInstanceFolder;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstanceFolderChangeCancellationArmed))]
    private bool _isMovingInstanceFolder;
    [ObservableProperty] private string _instanceFolderChangeMetricText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultInstanceFolder))]
    [NotifyPropertyChangedFor(nameof(CanResetInstanceFolder))]
    private string _instanceFolder = string.Empty;
    [ObservableProperty] private bool _isStorageUsageLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageUsageSummary))]
    private string _totalStorageUsage = "0 B";
    [ObservableProperty] private IReadOnlyList<StorageUsageSegment> _storageUsageItems = [];
    [ObservableProperty] private bool _hasAboutLatestCommit;
    [ObservableProperty] private bool _hasMoreAboutContributors;
    [ObservableProperty] private string _aboutLatestCommitSha = string.Empty;
    [ObservableProperty] private string _aboutLatestCommitHint = string.Empty;
    [ObservableProperty] private string _aboutContributorOverflow = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMirrorOperationError))]
    private string _mirrorOperationError = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMirrorOperationStatus))]
    private string _mirrorOperationStatus = string.Empty;
    [ObservableProperty] private string _mirrorUrl = string.Empty;
    [ObservableProperty] private string _manualMirrorJson = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddSourceChoiceVisible))]
    [NotifyPropertyChangedFor(nameof(IsAutomaticSourceVisible))]
    [NotifyPropertyChangedFor(nameof(IsManualSourceVisible))]
    private DownloadSourceAdditionStep _mirrorAdditionStep;
    [ObservableProperty] private bool _isAddingMirror;
    [ObservableProperty] private bool _isMirrorOperationBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingMirrorDelete))]
    private MirrorSourceViewModel? _pendingMirrorDelete;

    public SettingsViewModel(
        IDesktopSettingsStore settings,
        IExternalUriLauncher uriLauncher,
        StringLocalizer localizer,
        IFilePicker? filePicker = null,
        IGitHubClient? gitHubClient = null,
        IMirrorCatalog? mirrorCatalog = null,
        IMirrorDiscovery? mirrorDiscovery = null,
        IGameVersionCatalog? versionCatalog = null,
        IGameProcessTracker? gameProcess = null,
        IInstanceRepository? instanceRepository = null,
        IGpuProvider? gpuProvider = null,
        IProfileRepository? profileRepository = null,
        HttpClient? httpClient = null)
    {
        _settings = settings;
        _uriLauncher = uriLauncher;
        _localizer = localizer;
        _filePicker = filePicker;
        _gitHubClient = gitHubClient;
        _mirrorCatalog = mirrorCatalog;
        _mirrorDiscovery = mirrorDiscovery;
        _versionCatalog = versionCatalog;
        _gameProcess = gameProcess;
        _instanceRepository = instanceRepository;
        _profileRepository = profileRepository;
        _httpClient = httpClient;
        if (_instanceRepository is not null)
            _instanceRepository.InstancesChanged += OnInstancesChanged;
        if (_profileRepository is not null)
            _profileRepository.ProfilesChanged += OnProfilesChanged;

        Categories = new ObservableCollection<SettingCategoryViewModel>(
        [
            new("general", localizer["settings.general"], "general.png"),
            new("downloads", localizer["settings.downloads.title"], "downloads.png"),
            new("java", localizer["settings.java"], "java.png"),
            new("visual", localizer["settings.visual"], "visual.png"),
            new("network", localizer["settings.network"], "network.png"),
            new("data", localizer["settings.data"], "data.png"),
            new("about", localizer["settings.about"], "about.png")
        ]);
        Categories[0].IsSelected = true;

        Languages = new ObservableCollection<SettingChoiceViewModel>(
            localizer.AvailableLanguages.Select(language =>
                new SettingChoiceViewModel(language.Key, language.Value, GetFlagCountryCode(language.Key))));
        _detectedGpuAdapters = gpuProvider?.GetAdapters() ?? [];
        GpuPreferences = CreateGpuPreferences(localizer, _detectedGpuAdapters);
        AboutTeamMembers = new ObservableCollection<AboutTeamMemberViewModel>(
        [
            new("yyyumeniku", "YY", "creatorRole"),
            new("sanasol", "SA", "authRole"),
            new("Daniel Freak", "DF", "codevRole", "freakdaniel"),
            new("XargonWan", "XW", "cicdRole"),
            new("FowlBytez", "FB", "testerRole"),
            new("Aarav2709", "A", "siteRole")
        ]);

        _selectedLanguage = FindChoice(Languages, settings.Language);
        _selectedGpuPreference = ResolveGpuChoice(settings.GpuPreference);
        _closeAfterLaunch = settings.CloseAfterLaunch;
        _showAlphaMods = settings.ShowAlphaMods;
        _musicEnabled = settings.MusicEnabled;
        _disableNews = settings.DisableNews;
        _showDiscordAnnouncements = settings.ShowDiscordAnnouncements;
        _onlineMode = settings.OnlineMode;
        _useCustomJava = settings.UseCustomJava;
        _authDomain = ResolveAuthServer(settings.AuthDomain);
        IsOfficialProfile = _profileRepository?.GetSelectedProfile()?.IsOfficial == true;
        _customJavaPath = settings.CustomJavaPath;
        var persistedJavaArguments = settings.JavaArguments;
        _javaArguments = JvmArgumentBuilder.RemoveHeapArguments(persistedJavaArguments);
        DetectedSystemMemoryMb = Math.Max(4096, SystemMemoryProvider.GetSystemMemoryMb());
        MaximumJavaRamMb = Math.Max(
            MinimumJavaMemoryMb,
            Math.Floor(DetectedSystemMemoryMb * 0.75 / JavaMemoryStepMb) * JavaMemoryStepMb);
        _javaMaximumRamMb = NormalizeJavaMemory(
            JvmArgumentBuilder.ParseMaximumHeapMb(persistedJavaArguments) ?? 4096,
            MaximumJavaRamMb);
        var defaultInitialMemory = Math.Max(
            MinimumJavaMemoryMb,
            Math.Floor(_javaMaximumRamMb / 2 / JavaMemoryStepMb) * JavaMemoryStepMb);
        _javaInitialRamMb = NormalizeJavaMemory(
            JvmArgumentBuilder.ParseInitialHeapMb(persistedJavaArguments) ?? (int)defaultInitialMemory,
            _javaMaximumRamMb);
        ReplaceEnvironmentVariableItems(settings.GameEnvironmentVariables);
        _isGameRunning = gameProcess?.IsGameRunning() == true;
        if (gameProcess is not null)
        {
            gameProcess.GameProcessStarted += OnGameProcessStateChanged;
            gameProcess.GameProcessExited += OnGameProcessStateChanged;
        }

        RefreshLocalization();
        ReloadAuthServerItems();
        ReplaceJavaArgumentItems(_javaArguments);
        ReloadMirrorItems();
    }

    public ObservableCollection<SettingCategoryViewModel> Categories { get; }
    public ObservableCollection<SettingChoiceViewModel> Languages { get; }
    public ObservableCollection<SettingChoiceViewModel> GpuPreferences { get; }
    public ObservableCollection<AboutTeamMemberViewModel> AboutTeamMembers { get; }
    public ObservableCollection<AboutContributorViewModel> AboutContributors { get; } = [];
    public ObservableCollection<MirrorSourceViewModel> MirrorSources { get; } = [];
    public ObservableCollection<AuthServerItemViewModel> AuthServerItems { get; } = [];
    public ObservableCollection<JavaArgumentItemViewModel> JavaArgumentItems { get; } = [];
    public ObservableCollection<EnvironmentVariableItemViewModel> EnvironmentVariableItems { get; } = [];

    public int DetectedSystemMemoryMb { get; }
    public double MinimumJavaRamMb => MinimumJavaMemoryMb;
    public double MaximumJavaRamMb { get; }
    public double JavaMemoryTickFrequency => JavaMemoryStepMb;
    public double JavaInitialRamMaximum => JavaMaximumRamMb;
    public bool UseBundledJava => !UseCustomJava;
    public bool HasJavaArgumentsError => !string.IsNullOrWhiteSpace(JavaArgumentsError);
    public bool HasJavaArguments => JavaArgumentItems.Count > 0;
    public bool HasNoJavaArguments => JavaArgumentItems.Count == 0;
    public bool HasEnvironmentVariablesError => !string.IsNullOrWhiteSpace(EnvironmentVariablesError);
    public bool HasEnvironmentVariables => EnvironmentVariableItems.Count > 0;
    public bool HasNoEnvironmentVariables => EnvironmentVariableItems.Count == 0;
    public string JavaMaximumRamValue => FormatMemory(JavaMaximumRamMb);
    public string JavaInitialRamValue => FormatMemory(JavaInitialRamMb);
    public string StorageUsageSummary => $"{StorageUsedLabel} {TotalStorageUsage}";
    public bool HasMirrors => MirrorSources.Count > 0;
    public bool HasNoMirrors => MirrorSources.Count == 0;
    public bool HasMirrorOperationError => !string.IsNullOrWhiteSpace(MirrorOperationError);
    public bool HasMirrorOperationStatus => !string.IsNullOrWhiteSpace(MirrorOperationStatus);
    public bool HasPendingMirrorDelete => PendingMirrorDelete is not null;
    public bool IsAddSourceChoiceVisible => MirrorAdditionStep == DownloadSourceAdditionStep.ChooseMethod;
    public bool IsAutomaticSourceVisible => MirrorAdditionStep == DownloadSourceAdditionStep.Automatic;
    public bool IsManualSourceVisible => MirrorAdditionStep == DownloadSourceAdditionStep.Manual;
    public bool IsAuthServerVisible => OnlineMode && !IsOfficialProfile;
    public bool IsOfficialProfile { get; private set; }
    public bool HasAuthServers => AuthServerItems.Count > 0;
    public bool HasAuthServerAddStatus => !string.IsNullOrWhiteSpace(AuthServerAddStatus);
    public bool IsAuthServerCancellationArmed => IsCheckingAuthServer && _isAuthServerCancellationArmed;
    public bool CanChangeInstanceFolder => !IsGameRunning && !IsChangingInstanceFolder;
    public bool CanUseInstanceFolderChangeAction => !IsGameRunning;
    public bool IsDefaultInstanceFolder => DirectoriesEqual(InstanceFolder, _settings.DefaultInstanceDirectory);
    public bool CanResetInstanceFolder => CanChangeInstanceFolder && !IsDefaultInstanceFolder;
    public bool IsInstanceFolderChangeCancellationArmed =>
        IsChangingInstanceFolder &&
        IsMovingInstanceFolder &&
        _canCancelInstanceFolderChange &&
        _isInstanceFolderChangeCancellationArmed;

    public string PageTitle { get; private set; } = string.Empty;
    public string PageDescription { get; private set; } = string.Empty;
    public string BackLabel { get; private set; } = string.Empty;
    public string LanguageCategoryTitle { get; private set; } = string.Empty;
    public string GeneralTitle { get; private set; } = string.Empty;
    public string DownloadsTitle { get; private set; } = string.Empty;
    public string JavaTitle { get; private set; } = string.Empty;
    public string VisualTitle { get; private set; } = string.Empty;
    public string NetworkTitle { get; private set; } = string.Empty;
    public string DataTitle { get; private set; } = string.Empty;
    public string AboutTitle { get; private set; } = string.Empty;
    public string SaveLabel { get; private set; } = string.Empty;
    public string LanguageLabel { get; private set; } = string.Empty;
    public string LanguageHint { get; private set; } = string.Empty;
    public string CloseAfterLaunchLabel { get; private set; } = string.Empty;
    public string CloseAfterLaunchHint { get; private set; } = string.Empty;
    public string AlphaModsLabel { get; private set; } = string.Empty;
    public string AlphaModsHint { get; private set; } = string.Empty;
    public string DownloadsInfo { get; private set; } = string.Empty;
    public string DownloadsNoteTitle { get; private set; } = string.Empty;
    public string DownloadSourcesTitle { get; private set; } = string.Empty;
    public string SourceLinkColumn { get; private set; } = string.Empty;
    public string SourceTypeColumn { get; private set; } = string.Empty;
    public string SourceAvailabilityColumn { get; private set; } = string.Empty;
    public string SourcePingColumn { get; private set; } = string.Empty;
    public string SourceEnabledColumn { get; private set; } = string.Empty;
    public string OfficialSourceLink => "https://account-data.hytale.com";
    public string OfficialSourceType { get; private set; } = string.Empty;
    public string OfficialSourceAvailability { get; private set; } = string.Empty;
    public string OfficialSourcePing { get; private set; } = "—";
    public bool OfficialSourceIsChecking { get; private set; }
    public bool OfficialSourceIsAvailable { get; private set; }
    public bool OfficialSourceIsUnavailable { get; private set; }
    public bool OfficialSourceIsEnabled { get; private set; }
    public string AddSourceButtonLabel { get; private set; } = string.Empty;
    public string AddSourceLabel { get; private set; } = string.Empty;
    public string AddSourceTitle { get; private set; } = string.Empty;
    public string AddSourceHint { get; private set; } = string.Empty;
    public string MirrorUrlPlaceholder { get; private set; } = string.Empty;
    public string AddSourceMethodHint { get; private set; } = string.Empty;
    public string AutomaticSourceLabel { get; private set; } = string.Empty;
    public string AutomaticSourceHint { get; private set; } = string.Empty;
    public string ManualSourceLabel { get; private set; } = string.Empty;
    public string ManualSourceHint { get; private set; } = string.Empty;
    public string ManualSourceTitle { get; private set; } = string.Empty;
    public string ManualSourceDescription { get; private set; } = string.Empty;
    public string ManualSourceJsonLabel { get; private set; } = string.Empty;
    public string ManualSourceJsonPlaceholder { get; private set; } = string.Empty;
    public string CancelLabel { get; private set; } = string.Empty;
    public string RemoveLabel { get; private set; } = string.Empty;
    public string DeleteSourceTitle { get; private set; } = string.Empty;
    public string DeleteSourceHint { get; private set; } = string.Empty;
    public string MusicLabel { get; private set; } = string.Empty;
    public string MusicHint { get; private set; } = string.Empty;
    public string HideNewsLabel { get; private set; } = string.Empty;
    public string HideNewsHint { get; private set; } = string.Empty;
    public string DiscordAnnouncementsLabel { get; private set; } = string.Empty;
    public string DiscordAnnouncementsHint { get; private set; } = string.Empty;
    public string OnlineModeLabel { get; private set; } = string.Empty;
    public string OnlineModeHint { get; private set; } = string.Empty;
    public string AuthServerLabel { get; private set; } = string.Empty;
    public string AuthServerHint { get; private set; } = string.Empty;
    public string AuthServerPlaceholder { get; private set; } = string.Empty;
    public string AuthServerAddLabel { get; private set; } = string.Empty;
    public string AuthServerCheckingLabel { get; private set; } = string.Empty;
    public string AuthServerOnlineLabel { get; private set; } = string.Empty;
    public string AuthServerOfflineLabel { get; private set; } = string.Empty;
    public string AuthServerPingTemplate { get; private set; } = string.Empty;
    public string AuthServerOfflineWarning { get; private set; } = string.Empty;
    public string AuthServerErrorTooltip { get; private set; } = string.Empty;
    public string JavaRuntimeLabel { get; private set; } = string.Empty;
    public string BundledJavaLabel { get; private set; } = string.Empty;
    public string BundledJavaHint { get; private set; } = string.Empty;
    public string CustomJavaLabel { get; private set; } = string.Empty;
    public string CustomJavaHint { get; private set; } = string.Empty;
    public string EditLabel { get; private set; } = string.Empty;
    public string ChangeLabel { get; private set; } = string.Empty;
    public string CustomJavaPathEmptyState { get; private set; } = string.Empty;
    public bool HasCustomJavaPath => !string.IsNullOrWhiteSpace(CustomJavaPath);
    public string EditCustomJavaPathLabel => HasCustomJavaPath ? ChangeLabel : EditLabel;
    public string CustomJavaPathDisplay => HasCustomJavaPath ? CustomJavaPath : CustomJavaPathEmptyState;
    public string SelectLabel { get; private set; } = string.Empty;
    public string RamAllocationLabel { get; private set; } = string.Empty;
    public string MaximumRamLabel { get; private set; } = string.Empty;
    public string InitialRamLabel { get; private set; } = string.Empty;
    public string JavaArgumentsLabel { get; private set; } = string.Empty;
    public string JavaArgumentsHint { get; private set; } = string.Empty;
    public string JavaArgumentsEmptyState { get; private set; } = string.Empty;
    public string JavaArgumentsPlaceholder { get; private set; } = string.Empty;
    public string GpuLabel { get; private set; } = string.Empty;
    public string GpuHint { get; private set; } = string.Empty;
    public string GpuRowLabel { get; private set; } = string.Empty;
    public string EnvLabel { get; private set; } = string.Empty;
    public string EnvHint { get; private set; } = string.Empty;
    public string EnvEmptyState { get; private set; } = string.Empty;
    public string EnvPlaceholder { get; private set; } = string.Empty;
    public string InstanceFolderLabel { get; private set; } = string.Empty;
    public string LauncherFilesLabel { get; private set; } = string.Empty;
    public string StorageLoadingLabel { get; private set; } = string.Empty;
    public string StorageUsedLabel { get; private set; } = string.Empty;
    public string InstancesLabel { get; private set; } = string.Empty;
    public string ImagesLabel { get; private set; } = string.Empty;
    public string ModsLabel { get; private set; } = string.Empty;
    public string NewsLabel { get; private set; } = string.Empty;
    public string LogsLabel { get; private set; } = string.Empty;
    public string OtherFilesLabel { get; private set; } = string.Empty;
    public string GameDirectoryLabel { get; private set; } = string.Empty;
    public string GameDirectoryHint { get; private set; } = string.Empty;
    public string LauncherDataLabel { get; private set; } = string.Empty;
    public string LauncherDataFolderLabel { get; private set; } = string.Empty;
    public string LauncherDataHint { get; private set; } = string.Empty;
    public string LauncherDataFolder { get; private set; } = string.Empty;
    public string OpenFolderLabel { get; private set; } = string.Empty;
    public string ChangeInstanceFolderLabel { get; private set; } = string.Empty;
    public string ResetInstanceFolderLabel { get; private set; } = string.Empty;
    public string GameRunningWarning { get; private set; } = string.Empty;
    public string MovingDataLabel { get; private set; } = string.Empty;
    public string AboutDisclaimer { get; private set; } = string.Empty;
    public string AboutBuiltWithLabel { get; private set; } = string.Empty;
    public string BugReportLabel { get; private set; } = string.Empty;
    public string AboutProjectTitle { get; private set; } = string.Empty;
    public string AboutGitHubHint { get; private set; } = string.Empty;
    public string AboutDocumentationLabel { get; private set; } = string.Empty;
    public string AboutDocumentationHint { get; private set; } = string.Empty;
    public string AboutCommunityTitle { get; private set; } = string.Empty;
    public string AboutDiscordHint { get; private set; } = string.Empty;
    public string AboutBugReportHint { get; private set; } = string.Empty;
    public string AboutTeamTitle { get; private set; } = string.Empty;
    public string AboutTeamHint { get; private set; } = string.Empty;
    public string AboutLegalTitle { get; private set; } = string.Empty;
    public string AboutLicenseLabel { get; private set; } = string.Empty;
    public string AboutLicenseHint { get; private set; } = string.Empty;
    public string AboutCreditsLabel { get; private set; } = string.Empty;
    public string AboutCreditsHint { get; private set; } = string.Empty;
    public string AboutLordiconLabel { get; private set; } = string.Empty;
    public string AboutLordiconHint { get; private set; } = string.Empty;
    public string AboutCurrentVersionLabel { get; private set; } = string.Empty;
    public string AboutCurrentVersionHint { get; private set; } = string.Empty;
    public string AboutCurrentVersion { get; private set; } = string.Empty;
    public string AboutLatestCommitLabel { get; private set; } = string.Empty;
    public string AboutContributorsTitle { get; private set; } = string.Empty;
    public string AboutHytaleEulaLabel { get; private set; } = string.Empty;
    public string AboutHytaleEulaHint { get; private set; } = string.Empty;

    public string ActiveCategoryTitle =>
        Categories.FirstOrDefault(category => category.Id == SelectedCategory)?.Label ?? GeneralTitle;

    public bool IsGeneral => SelectedCategory == "general";
    public bool IsDownloads => SelectedCategory == "downloads";
    public bool IsJava => SelectedCategory == "java";
    public bool IsVisual => SelectedCategory == "visual";
    public bool IsNetwork => SelectedCategory == "network";
    public bool IsData => SelectedCategory == "data";
    public bool IsAbout => SelectedCategory == "about";
    public void RefreshLocalization()
    {
        PageTitle = _localizer["dock.settings"];
        PageDescription = _localizer["desktopSettings.description"];
        BackLabel = _localizer["common.back"];
        LanguageCategoryTitle = _localizer["desktopSettings.categories.language"];
        GeneralTitle = _localizer["desktopSettings.categories.miscellaneous"];
        DownloadsTitle = _localizer["settings.downloads.title"];
        JavaTitle = _localizer["settings.java"];
        VisualTitle = _localizer["settings.visualSettings.title"];
        NetworkTitle = _localizer["settings.network"];
        DataTitle = _localizer["settings.dataSettings.title"];
        AboutTitle = _localizer["settings.aboutSettings.title"];
        SaveLabel = _localizer["common.save"];
        LanguageLabel = _localizer["settings.languageSettings.interfaceLanguage"];
        LanguageHint = _localizer["settings.languageSettings.interfaceLanguageHint"];
        CloseAfterLaunchLabel = _localizer["settings.generalSettings.closeLauncher"];
        CloseAfterLaunchHint = _localizer["settings.generalSettings.closeLauncherHint"];
        AlphaModsLabel = _localizer["settings.generalSettings.showAlphaMods"];
        AlphaModsHint = _localizer["settings.generalSettings.showAlphaModsHint"];
        DownloadsInfo = _localizer["settings.downloads.howDownloadsWorkDescription"];
        DownloadsNoteTitle = _localizer["common.note"];
        DownloadSourcesTitle = _localizer["settings.downloads.sources"];
        SourceLinkColumn = _localizer["settings.downloads.columnLink"];
        SourceTypeColumn = _localizer["settings.downloads.columnType"];
        SourceAvailabilityColumn = _localizer["settings.downloads.columnAvailability"];
        SourcePingColumn = _localizer["settings.downloads.columnPing"];
        SourceEnabledColumn = _localizer["settings.downloads.columnEnabled"];
        OfficialSourceType = _localizer["settings.downloads.sourceTypeOfficial"];
        OfficialSourceAvailability = GetOfficialSourceAvailabilityLabel();
        AddSourceButtonLabel = _localizer["settings.downloads.add"];
        AddSourceLabel = _localizer["settings.downloads.addSource"];
        AddSourceTitle = _localizer["settings.downloads.addSourceTitle"];
        AddSourceHint = _localizer["settings.downloads.addSourceHint"];
        MirrorUrlPlaceholder = _localizer["settings.downloads.sourceUrlPlaceholder"];
        AddSourceMethodHint = _localizer["settings.downloads.addSourceMethodHint"];
        AutomaticSourceLabel = _localizer["settings.downloads.addSourceAutomatic"];
        AutomaticSourceHint = _localizer["settings.downloads.addSourceAutomaticHint"];
        ManualSourceLabel = _localizer["settings.downloads.addSourceManual"];
        ManualSourceHint = _localizer["settings.downloads.addSourceManualHint"];
        ManualSourceTitle = _localizer["settings.downloads.manualSourceTitle"];
        ManualSourceDescription = _localizer["settings.downloads.manualSourceHint"];
        ManualSourceJsonLabel = _localizer["settings.downloads.manualSourceJson"];
        ManualSourceJsonPlaceholder = _localizer["settings.downloads.manualSourceJsonPlaceholder"];
        CancelLabel = _localizer["common.cancel"];
        RemoveLabel = _localizer["common.remove"];
        DeleteSourceTitle = _localizer["settings.downloads.deleteSourceTitle"];
        DeleteSourceHint = _localizer["settings.downloads.deleteSourceHint"];
        MusicLabel = _localizer["desktopSettings.music"];
        MusicHint = _localizer["desktopSettings.musicHint"];
        HideNewsLabel = _localizer["settings.visualSettings.hideNews"];
        HideNewsHint = _localizer["settings.visualSettings.hideNewsHint"];
        DiscordAnnouncementsLabel = _localizer["discord.showAnnouncements"];
        DiscordAnnouncementsHint = _localizer["desktopSettings.discordAnnouncementsHint"];
        OnlineModeLabel = _localizer["settings.networkSettings.onlineMode"];
        OnlineModeHint = _localizer["settings.networkSettings.onlineModeHint"];
        AuthServerLabel = _localizer["settings.networkSettings.authServer"];
        AuthServerHint = _localizer["settings.networkSettings.authServerHint"];
        AuthServerPlaceholder = _localizer["settings.networkSettings.authServerPlaceholder"];
        AuthServerAddLabel = _localizer["settings.downloads.add"];
        AuthServerCheckingLabel = _localizer["settings.networkSettings.authServerChecking"];
        AuthServerOnlineLabel = _localizer["settings.networkSettings.authServerOnline"];
        AuthServerOfflineLabel = _localizer["settings.networkSettings.authServerOffline"];
        AuthServerPingTemplate = _localizer["settings.networkSettings.authServerPing"];
        AuthServerOfflineWarning = _localizer["settings.networkSettings.authServerOfflineWarning"];
        AuthServerErrorTooltip = _localizer["settings.networkSettings.authServerErrorTooltip"];
        JavaRuntimeLabel = _localizer["settings.javaSettings.javaRuntime"];
        BundledJavaLabel = _localizer["settings.javaSettings.useBundledJava"];
        BundledJavaHint = _localizer["settings.javaSettings.useBundledJavaHint"];
        CustomJavaLabel = _localizer["settings.javaSettings.useCustomJava"];
        CustomJavaHint = _localizer["settings.javaSettings.useCustomJavaHint"];
        EditLabel = _localizer["common.edit"];
        ChangeLabel = _localizer["common.change"];
        CustomJavaPathEmptyState = _localizer["settings.javaSettings.customJavaPathEmpty"];
        RamAllocationLabel = _localizer["settings.javaSettings.ramAllocation"];
        MaximumRamLabel = _localizer["settings.javaSettings.maxRam"];
        InitialRamLabel = _localizer["settings.javaSettings.initialRam"];
        JavaArgumentsLabel = _localizer["settings.javaSettings.jvmArguments"];
        JavaArgumentsHint = _localizer["settings.javaSettings.jvmArgumentsHint"];
        JavaArgumentsEmptyState = _localizer["settings.javaSettings.jvmArgumentsEmpty"];
        JavaArgumentsPlaceholder = _localizer["settings.javaSettings.jvmArgumentsPlaceholder"];
        GpuLabel = _localizer["settings.graphicsSettings.gpuPreference"];
        GpuHint = _localizer["settings.graphicsSettings.gpuPreferenceHint"];
        GpuRowLabel = _localizer["settings.graphicsSettings.usedGpu"];
        EnvLabel = _localizer["settings.variablesSettings.customEnvVars"];
        EnvHint = _localizer["settings.variablesSettings.customEnvVarsHint"];
        EnvEmptyState = _localizer["settings.variablesSettings.customEnvVarsEmpty"];
        EnvPlaceholder = _localizer["settings.variablesSettings.customEnvVarsPlaceholder"];
        InstanceFolderLabel = _localizer["settings.dataSettings.instanceFolder"];
        LauncherFilesLabel = _localizer["settings.dataSettings.launcherFiles"];
        StorageLoadingLabel = _localizer["common.loading"];
        StorageUsedLabel = _localizer["settings.dataSettings.storageUsed"];
        InstancesLabel = _localizer["dock.instances"];
        ImagesLabel = _localizer["settings.visualSettings.images"];
        ModsLabel = _localizer["instances.tab.mods"];
        NewsLabel = _localizer["dock.news"];
        LogsLabel = _localizer["dock.logs"];
        OtherFilesLabel = _localizer["settings.dataSettings.otherFiles"];
        GameDirectoryLabel = _localizer["settings.dataSettings.gameDirectory"];
        GameDirectoryHint = _localizer["settings.dataSettings.gameDirectoryHint"];
        LauncherDataLabel = _localizer["settings.dataSettings.launcherData"];
        LauncherDataFolderLabel = _localizer["settings.dataSettings.launcherDataFolder"];
        LauncherDataHint = _localizer["settings.dataSettings.launcherDataHint"];
        LauncherDataFolder = _settings.LauncherDataDirectory;
        OpenFolderLabel = _localizer["settings.dataSettings.open"];
        ChangeInstanceFolderLabel = _localizer["common.edit"];
        ResetInstanceFolderLabel = _localizer["common.reset"];
        GameRunningWarning = _localizer["settings.dataSettings.gameRunningWarning"];
        MovingDataLabel = _localizer["settings.dataSettings.movingData"];
        InstanceFolder = string.IsNullOrWhiteSpace(_settings.InstanceDirectory)
            ? _settings.DefaultInstanceDirectory
            : _settings.InstanceDirectory;
        if (_latestStorageUsage is not null)
            ApplyStorageUsage(_latestStorageUsage);
        AboutDisclaimer = _localizer["settings.aboutSettings.disclaimer"];
        AboutBuiltWithLabel = _localizer["settings.aboutSettings.builtWith"];
        BugReportLabel = _localizer["settings.aboutSettings.bugReport"];
        AboutProjectTitle = _localizer["settings.aboutSettings.project"];
        AboutGitHubHint = _localizer["settings.aboutSettings.githubHint"];
        AboutDocumentationLabel = _localizer["settings.aboutSettings.documentation"];
        AboutDocumentationHint = _localizer["settings.aboutSettings.documentationHint"];
        AboutCommunityTitle = _localizer["settings.aboutSettings.community"];
        AboutDiscordHint = _localizer["settings.aboutSettings.discordHint"];
        AboutBugReportHint = _localizer["settings.aboutSettings.bugReportHint"];
        AboutTeamTitle = _localizer["settings.aboutSettings.coreTeam"];
        AboutTeamHint = _localizer["settings.aboutSettings.coreTeamDescription"];
        AboutLegalTitle = _localizer["settings.aboutSettings.legal"];
        AboutLicenseLabel = _localizer["settings.aboutSettings.license"];
        AboutLicenseHint = _localizer["settings.aboutSettings.licenseHint"];
        AboutCreditsLabel = _localizer["settings.aboutSettings.credits"];
        AboutCreditsHint = _localizer["settings.aboutSettings.creditsHint"];
        AboutLordiconLabel = _localizer["settings.aboutSettings.lordicon"];
        AboutLordiconHint = _localizer["settings.aboutSettings.lordiconHint"];
        AboutCurrentVersionLabel = _localizer["settings.aboutSettings.currentVersion"];
        AboutCurrentVersionHint = _localizer["settings.aboutSettings.currentVersionHint"];
        AboutCurrentVersion = DesktopApplicationInfo.Version;
        AboutLatestCommitLabel = _localizer["settings.aboutSettings.latestMainCommit"];
        AboutContributorsTitle = _localizer["settings.aboutSettings.contributors"];
        AboutHytaleEulaLabel = _localizer["settings.aboutSettings.hytaleEula"];
        AboutHytaleEulaHint = _localizer["settings.aboutSettings.hytaleEulaHint"];
        AboutLatestCommitHint = _latestMainCommit?.Message ?? _localizer[
            _aboutDataLoaded
                ? "settings.aboutSettings.latestMainCommitUnavailable"
                : "settings.aboutSettings.latestMainCommitLoading"];

        foreach (var member in AboutTeamMembers)
            member.Role = _localizer[$"settings.aboutSettings.{member.RoleKey}"];

        UpdateCategoryLabel("general", _localizer["settings.general"]);
        UpdateCategoryLabel("downloads", _localizer["settings.downloads.title"]);
        UpdateCategoryLabel("java", _localizer["settings.java"]);
        UpdateCategoryLabel("visual", _localizer["desktopSettings.categories.appearance"]);
        UpdateCategoryLabel("network", _localizer["settings.network"]);
        UpdateCategoryLabel("data", _localizer["settings.data"]);
        UpdateCategoryLabel("about", _localizer["settings.about"]);
        UpdateCategoryDescription("general", _localizer["settings.categoryDescriptions.general"]);
        UpdateCategoryDescription("downloads", _localizer["settings.categoryDescriptions.downloads"]);
        UpdateCategoryDescription("java", _localizer["settings.categoryDescriptions.java"]);
        UpdateCategoryDescription("visual", _localizer["settings.categoryDescriptions.visual"]);
        UpdateCategoryDescription("network", _localizer["settings.categoryDescriptions.network"]);
        UpdateCategoryDescription("data", _localizer["settings.categoryDescriptions.data"]);
        UpdateCategoryDescription("about", _localizer["settings.categoryDescriptions.about"]);
        UpdateChoiceDisplay(GpuPreferences, "auto", _localizer["settings.graphicsSettings.gpu_auto"]);
        foreach (var mirror in MirrorSources)
        {
            mirror.UpdateSourceType(GetMirrorSourceType(mirror.Definition));
            mirror.RefreshAvailabilityLabel(
                _localizer["settings.downloads.checkingAvailability"],
                _localizer["settings.downloads.sourceDisabledState"],
                _localizer["settings.downloads.sourceAvailable"],
                _localizer["settings.downloads.sourceNoCompatibleVersions"],
                _localizer["settings.downloads.sourceUnavailable"]);
        }
        foreach (var server in AuthServerItems)
        {
            server.RefreshAvailabilityLabel(
                AuthServerCheckingLabel,
                AuthServerOnlineLabel,
                AuthServerOfflineLabel);
        }

        OnPropertyChanged(string.Empty);
    }

    partial void OnSelectedLanguageChanged(SettingChoiceViewModel value)
    {
        if (value is null ||
            string.Equals(_localizer.CurrentLanguage, value.Value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_localizer.SetLanguage(value.Value))
            _settings.Language = _localizer.CurrentLanguage;
    }
    partial void OnSelectedGpuPreferenceChanged(SettingChoiceViewModel value) => _settings.GpuPreference = value.Value;
    partial void OnCloseAfterLaunchChanged(bool value) => _settings.CloseAfterLaunch = value;
    partial void OnShowAlphaModsChanged(bool value) => _settings.ShowAlphaMods = value;
    partial void OnMusicEnabledChanged(bool value) => _settings.MusicEnabled = value;
    partial void OnDisableNewsChanged(bool value) => _settings.DisableNews = value;
    partial void OnShowDiscordAnnouncementsChanged(bool value) => _settings.ShowDiscordAnnouncements = value;
    partial void OnOnlineModeChanged(bool value)
    {
        _settings.OnlineMode = value;
        if (value && IsNetwork)
            EnsureAuthServersProbed();
    }
    partial void OnNewAuthServerChanged(string value)
    {
        if (!IsAuthServerAddError && string.IsNullOrWhiteSpace(AuthServerAddStatus))
            return;

        IsAuthServerAddError = false;
        AuthServerAddStatus = string.Empty;
    }
    partial void OnUseCustomJavaChanged(bool value)
    {
        _settings.UseCustomJava = value;
    }

    partial void OnCustomJavaPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasCustomJavaPath));
        OnPropertyChanged(nameof(EditCustomJavaPathLabel));
        OnPropertyChanged(nameof(CustomJavaPathDisplay));
    }
    partial void OnJavaArgumentsChanged(string value)
    {
        if (_updatingJavaArguments)
            return;

        var withoutHeap = JvmArgumentBuilder.RemoveHeapArguments(value);
        if (JvmArgumentBuilder.ContainsHeapArguments(value))
        {
            _updatingJavaArguments = true;
            try
            {
                JavaArguments = withoutHeap;
            }
            finally
            {
                _updatingJavaArguments = false;
            }

            JavaArgumentsError = _localizer["settings.javaSettings.jvmMemoryArgumentsManaged"];
            ReplaceJavaArgumentItems(withoutHeap);
            return;
        }

        JavaArgumentsError = string.Empty;
        ReplaceJavaArgumentItems(withoutHeap);
    }
    partial void OnJavaMaximumRamMbChanged(double value) => PersistJavaMemory();
    partial void OnJavaInitialRamMbChanged(double value) => PersistJavaMemory();

    [RelayCommand]
    private void SelectCategory(SettingCategoryViewModel? category)
    {
        if (category is null || string.Equals(SelectedCategory, category.Id, StringComparison.Ordinal))
            return;

        SelectedCategory = category.Id;
        foreach (var item in Categories)
            item.IsSelected = ReferenceEquals(item, category);
        StatusMessage = string.Empty;

        if (string.Equals(category.Id, "about", StringComparison.Ordinal))
            _ = LoadAboutDataAsync();
        else if (string.Equals(category.Id, "downloads", StringComparison.Ordinal))
            EnsureDownloadSourcesProbed();
        else if (string.Equals(category.Id, "network", StringComparison.Ordinal))
            EnsureAuthServersProbed();
        else if (string.Equals(category.Id, "data", StringComparison.Ordinal))
            EnsureStorageUsageLoaded();
    }

    [RelayCommand]
    private void ShowAddMirror()
    {
        MirrorAdditionStep = DownloadSourceAdditionStep.ChooseMethod;
        IsAddingMirror = true;
        PendingMirrorDelete = null;
        MirrorUrl = string.Empty;
        ManualMirrorJson = string.Empty;
        MirrorOperationError = string.Empty;
        MirrorOperationStatus = string.Empty;
    }

    [RelayCommand]
    private void BeginAutomaticMirrorAddition()
    {
        MirrorAdditionStep = DownloadSourceAdditionStep.Automatic;
        MirrorOperationError = string.Empty;
    }

    [RelayCommand]
    private void BeginManualMirrorAddition()
    {
        MirrorAdditionStep = DownloadSourceAdditionStep.Manual;
        MirrorOperationError = string.Empty;
    }

    [RelayCommand]
    private void ReturnToMirrorAdditionChoice()
    {
        MirrorAdditionStep = DownloadSourceAdditionStep.ChooseMethod;
        MirrorOperationError = string.Empty;
    }

    [RelayCommand]
    private void CancelAddMirror()
    {
        IsAddingMirror = false;
    }

    internal void CompleteMirrorAdditionTransition()
    {
        if (IsAddingMirror)
            return;

        MirrorAdditionStep = DownloadSourceAdditionStep.None;
        MirrorUrl = string.Empty;
        ManualMirrorJson = string.Empty;
        MirrorOperationError = string.Empty;
    }

    [RelayCommand]
    private async Task AddMirror()
    {
        if (IsMirrorOperationBusy)
            return;

        if (_mirrorCatalog is null || _mirrorDiscovery is null || _versionCatalog is null)
        {
            MirrorOperationError = _localizer["settings.downloads.sourceManagementUnavailable"];
            return;
        }

        var normalizedUrl = NormalizeMirrorUrl(MirrorUrl);
        if (!IsAllowedMirrorUrl(normalizedUrl, out var uri))
        {
            MirrorOperationError = _localizer["settings.downloads.invalidSourceUrl"];
            return;
        }

        if (MirrorSources.Any(source => EndpointsEqual(source.Endpoint, uri)))
        {
            MirrorOperationError = _localizer["settings.downloads.sourceAlreadyExists"];
            return;
        }

        IsMirrorOperationBusy = true;
        MirrorOperationError = string.Empty;
        MirrorOperationStatus = _localizer["settings.downloads.detectingSource"];
        try
        {
            var result = await _mirrorDiscovery.DiscoverMirrorAsync(normalizedUrl);
            if (!result.Success || result.Mirror is null)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                    Logger.Debug("Settings", $"Download source discovery failed: {result.Error}");
                MirrorOperationError = _localizer["settings.downloads.sourceDetectionFailed"];
                return;
            }

            var existing = _mirrorCatalog.GetAll();
            result.Mirror.Id = CreateUniqueMirrorId(result.Mirror.Id, existing.Select(item => item.Id));
            result.Mirror.Priority = existing.Count == 0
                ? 100
                : Math.Max(100, existing.Max(item => item.Priority) + 10);
            result.Mirror.Enabled = true;
            _mirrorCatalog.Save(result.Mirror);
            _versionCatalog.ReloadMirrorSources();

            IsAddingMirror = false;
            MirrorOperationStatus = _localizer["settings.downloads.sourceAdded"];
            ReloadMirrorItems(clearStatus: false);
        }
        catch (OperationCanceledException)
        {
            MirrorOperationError = _localizer["settings.downloads.sourceDetectionFailed"];
        }
        catch (Exception ex)
        {
            Logger.Warning("Settings", $"Failed to add download source: {ex.Message}");
            MirrorOperationError = _localizer["settings.downloads.sourceSaveFailed"];
        }
        finally
        {
            IsMirrorOperationBusy = false;
        }
    }

    [RelayCommand]
    private void AddManualMirror()
    {
        if (IsMirrorOperationBusy)
            return;

        if (_mirrorCatalog is null || _versionCatalog is null)
        {
            MirrorOperationError = _localizer["settings.downloads.sourceManagementUnavailable"];
            return;
        }

        MirrorOperationError = string.Empty;
        try
        {
            var mirror = JsonSerializer.Deserialize<MirrorMeta>(
                ManualMirrorJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (mirror is null || !TryGetMirrorEndpoint(mirror, out var endpoint))
            {
                MirrorOperationError = _localizer["settings.downloads.invalidSourceJson"];
                return;
            }

            var existing = _mirrorCatalog.GetAll();
            if (existing.Any(source => string.Equals(source.Id, mirror.Id, StringComparison.OrdinalIgnoreCase)) ||
                MirrorSources.Any(source => EndpointsEqual(source.Endpoint, endpoint)))
            {
                MirrorOperationError = _localizer["settings.downloads.sourceAlreadyExists"];
                return;
            }

            mirror.Priority = Math.Max(100, mirror.Priority);
            _mirrorCatalog.Save(mirror);
            _versionCatalog.ReloadMirrorSources();

            IsAddingMirror = false;
            MirrorOperationStatus = _localizer["settings.downloads.sourceAdded"];
            ReloadMirrorItems(clearStatus: false);
        }
        catch (JsonException exception)
        {
            Logger.Debug("Settings", $"Invalid manual download source JSON: {exception.Message}");
            MirrorOperationError = _localizer["settings.downloads.invalidSourceJson"];
        }
        catch (ArgumentException exception)
        {
            Logger.Debug("Settings", $"Invalid manual download source definition: {exception.Message}");
            MirrorOperationError = _localizer["settings.downloads.invalidSourceJson"];
        }
        catch (Exception exception)
        {
            Logger.Warning("Settings", $"Failed to add manual download source: {exception.Message}");
            MirrorOperationError = _localizer["settings.downloads.sourceSaveFailed"];
        }
    }

    [RelayCommand]
    private void RequestDeleteMirror(MirrorSourceViewModel? mirror)
    {
        PendingMirrorDelete = mirror;
        IsAddingMirror = false;
        MirrorOperationError = string.Empty;
    }

    [RelayCommand]
    private void CancelDeleteMirror() => PendingMirrorDelete = null;

    [RelayCommand]
    private void ConfirmDeleteMirror()
    {
        if (PendingMirrorDelete is null || _mirrorCatalog is null || _versionCatalog is null)
            return;

        try
        {
            _mirrorCatalog.Delete(PendingMirrorDelete.Id);
            _versionCatalog.ReloadMirrorSources();
            PendingMirrorDelete = null;
            MirrorOperationStatus = _localizer["settings.downloads.sourceRemoved"];
            ReloadMirrorItems(clearStatus: false);
        }
        catch (Exception ex)
        {
            Logger.Warning("Settings", $"Failed to remove download source: {ex.Message}");
            MirrorOperationError = _localizer["settings.downloads.sourceRemoveFailed"];
        }
    }

    [RelayCommand]
    private void SaveNetwork()
    {
        _settings.AuthDomain = AuthDomain;
        ShowSaved();
    }

    [RelayCommand]
    private void ShowAddAuthServer()
    {
        NewAuthServer = string.Empty;
        AuthServerAddStatus = string.Empty;
        IsAuthServerAddError = false;
        IsAddingAuthServer = true;
    }

    [RelayCommand]
    private void CancelAddAuthServer()
    {
        CancelAuthServerAddition();
        NewAuthServer = string.Empty;
        AuthServerAddStatus = string.Empty;
        IsAuthServerAddError = false;
        IsAddingAuthServer = false;
    }

    private bool CanAddAuthServer()
        => IsCheckingAuthServer || !string.IsNullOrWhiteSpace(NewAuthServer);

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanAddAuthServer))]
    private async Task AddAuthServer()
    {
        if (IsCheckingAuthServer)
        {
            if (IsAuthServerCancellationArmed)
                CancelAuthServerAddition();

            return;
        }

        var value = NormalizeAuthServer(NewAuthServer);
        if (string.IsNullOrWhiteSpace(value))
            return;

        _isAuthServerCancellationArmed = false;
        OnPropertyChanged(nameof(IsAuthServerCancellationArmed));
        IsCheckingAuthServer = true;
        AuthServerAddStatus = string.Empty;
        IsAuthServerAddError = false;
        var cancellation = new CancellationTokenSource();
        var previousCancellation = _authServerAddCancellation;
        _authServerAddCancellation = cancellation;
        previousCancellation?.Cancel();

        try
        {
            var result = await CheckAuthServerAsync(value, cancellation.Token);
            if (!result.IsAvailable)
            {
                AuthServerAddStatus = AuthServerOfflineWarning;
                IsAuthServerAddError = true;
                return;
            }

            var existing = AuthServerItems.FirstOrDefault(server =>
                string.Equals(server.Value, value, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new AuthServerItemViewModel(
                    value,
                    isBuiltIn: false,
                    isSelected: false,
                    checkingLabel: AuthServerCheckingLabel,
                    selected: SelectAuthServer);
                AuthServerItems.Add(existing);
                UpdateAuthServerRows();
            }

            existing.ApplyProbe(
                result.IsAvailable,
                result.PingMs,
                AuthServerOnlineLabel,
                AuthServerOfflineLabel,
                AuthServerPingTemplate);
            SelectAuthServer(existing);
            PersistAuthServers();
            NewAuthServer = string.Empty;
            AuthServerAddStatus = string.Empty;
            IsAuthServerAddError = false;
            IsAddingAuthServer = false;
            ShowSaved();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Debug("Settings", "Auth server addition was canceled");
        }
        finally
        {
            _isAuthServerCancellationArmed = false;
            OnPropertyChanged(nameof(IsAuthServerCancellationArmed));
            IsCheckingAuthServer = false;
            if (ReferenceEquals(_authServerAddCancellation, cancellation))
                _authServerAddCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelAuthServerAddition()
    {
        try
        {
            _authServerAddCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _authServerAddCancellation = null;
        }
    }

    public void ArmAuthServerCancellation()
    {
        if (!IsCheckingAuthServer || _isAuthServerCancellationArmed)
            return;

        _isAuthServerCancellationArmed = true;
        OnPropertyChanged(nameof(IsAuthServerCancellationArmed));
        AddAuthServerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveAuthServer(AuthServerItemViewModel? server)
    {
        if (server is null || server.IsBuiltIn || !AuthServerItems.Remove(server))
            return;

        if (server.IsSelected)
            SelectAuthServer(AuthServerItems[0]);

        UpdateAuthServerRows();
        PersistAuthServers();
        ShowSaved();
    }

    private void SelectAuthServer(AuthServerItemViewModel? server)
    {
        if (server is null || !AuthServerItems.Contains(server))
            return;

        foreach (var item in AuthServerItems)
            item.SetSelectedWithoutNotification(ReferenceEquals(item, server));

        AuthDomain = server.Value;
        _settings.AuthDomain = server.Value;
        OnPropertyChanged(nameof(AuthDomain));
    }

    private void ReloadAuthServerItems()
    {
        _authServerProbeStarted = false;
        _authServerProbeCancellation?.Cancel();
        AuthServerItems.Clear();
        AuthServerItems.Add(new AuthServerItemViewModel(
            DefaultAuthServer,
            isBuiltIn: true,
            isSelected: string.Equals(AuthDomain, DefaultAuthServer, StringComparison.OrdinalIgnoreCase),
            checkingLabel: AuthServerCheckingLabel,
            selected: SelectAuthServer));

        var configuredServers = _settings.AuthServers ?? [];
        foreach (var configuredServer in configuredServers)
        {
            var normalized = NormalizeAuthServer(configuredServer);
            if (string.IsNullOrWhiteSpace(normalized) ||
                AuthServerItems.Any(server => string.Equals(server.Value, normalized, StringComparison.OrdinalIgnoreCase)))
                continue;

            AuthServerItems.Add(new AuthServerItemViewModel(
                normalized,
                isBuiltIn: false,
                isSelected: string.Equals(AuthDomain, normalized, StringComparison.OrdinalIgnoreCase),
                checkingLabel: AuthServerCheckingLabel,
                selected: SelectAuthServer));
        }

        if (!AuthServerItems.Any(server => server.IsSelected))
        {
            var active = AuthServerItems.FirstOrDefault(server =>
                string.Equals(server.Value, AuthDomain, StringComparison.OrdinalIgnoreCase));
            if (active is null && !string.Equals(AuthDomain, DefaultAuthServer, StringComparison.OrdinalIgnoreCase))
            {
                active = new AuthServerItemViewModel(
                    AuthDomain,
                    isBuiltIn: false,
                    isSelected: true,
                    checkingLabel: AuthServerCheckingLabel,
                    selected: SelectAuthServer);
                AuthServerItems.Add(active);
            }

            SelectAuthServer(active ?? AuthServerItems[0]);
        }

        UpdateAuthServerRows();
        if (IsNetwork && IsAuthServerVisible)
            EnsureAuthServersProbed();
    }

    private void UpdateAuthServerRows()
    {
        for (var index = 0; index < AuthServerItems.Count; index++)
            AuthServerItems[index].IsLast = index == AuthServerItems.Count - 1;

        OnPropertyChanged(nameof(HasAuthServers));
    }

    private void PersistAuthServers()
        => _settings.AuthServers = AuthServerItems
            .Where(server => !server.IsBuiltIn)
            .Select(server => server.Value)
            .ToArray();

    private void OnProfilesChanged()
    {
        var isOfficial = _profileRepository?.GetSelectedProfile()?.IsOfficial == true;
        if (IsOfficialProfile == isOfficial)
            return;

        IsOfficialProfile = isOfficial;
        OnPropertyChanged(nameof(IsOfficialProfile));
        OnPropertyChanged(nameof(IsAuthServerVisible));

        if (!isOfficial)
        {
            AuthDomain = ResolveAuthServer(_settings.AuthDomain);
            ReloadAuthServerItems();
            OnPropertyChanged(nameof(AuthDomain));
        }
    }

    private static string NormalizeAuthServer(string? value)
        => value?.Trim().TrimEnd('/') ?? string.Empty;

    private static string ResolveAuthServer(string? value)
        => NormalizeAuthServer(value) is { Length: > 0 } normalized
            ? normalized
            : DefaultAuthServer;

    [RelayCommand]
    private void SelectBundledJava() => UseCustomJava = false;

    [RelayCommand]
    private void SelectCustomJava() => UseCustomJava = true;

    [RelayCommand]
    private async Task BrowseJava()
    {
        if (_filePicker is null || IsBrowsingCustomJava)
            return;

        IsBrowsingCustomJava = true;
        try
        {
            var selectedPath = await _filePicker.BrowseJavaExecutableAsync(ResolveCustomJavaPickerDirectory());
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            // The path is applied only through this validated pick, so a stale or
            // hand-typed value can never persist after the instance folder moves
            CustomJavaPath = selectedPath;
            _settings.CustomJavaPath = selectedPath;
            UseCustomJava = true;
            ShowSaved();
        }
        finally
        {
            IsBrowsingCustomJava = false;
        }
    }

    private string ResolveCustomJavaPickerDirectory()
        => Path.GetDirectoryName(CustomJavaPath) is { Length: > 0 } directory && Directory.Exists(directory)
            ? directory
            : _settings.LauncherDataDirectory;

    [RelayCommand]
    private Task OpenCustomJavaFolder()
        => _uriLauncher.LaunchDirectoryAsync(
            Path.GetDirectoryName(CustomJavaPath) is { Length: > 0 } directory
                ? directory
                : _settings.LauncherDataDirectory);

    [RelayCommand]
    private void SaveJavaArguments()
    {
        var withoutHeap = JvmArgumentBuilder.RemoveHeapArguments(JavaArguments);
        var sanitized = JvmArgumentBuilder.Sanitize(withoutHeap);
        var containedBlockedArguments =
            !string.Equals(sanitized, withoutHeap.Trim(), StringComparison.Ordinal);

        JavaArguments = sanitized;
        JavaArgumentsError = containedBlockedArguments
            ? _localizer["settings.javaSettings.jvmArgumentsBlocked"]
            : string.Empty;
        _settings.JavaArguments = BuildPersistedJavaArguments(sanitized);
        ShowSaved();
    }

    [RelayCommand]
    private void ShowAddJavaArgument()
    {
        NewJavaArgument = string.Empty;
        JavaArgumentsError = string.Empty;
        IsAddingJavaArgument = true;
    }

    [RelayCommand]
    private void CancelAddJavaArgument()
    {
        NewJavaArgument = string.Empty;
        IsAddingJavaArgument = false;
    }

    private bool CanAddJavaArgument()
        => !string.IsNullOrWhiteSpace(NewJavaArgument);

    [RelayCommand(CanExecute = nameof(CanAddJavaArgument))]
    private void AddJavaArgument()
    {
        var withoutHeap = JvmArgumentBuilder.RemoveHeapArguments(NewJavaArgument);
        var sanitized = JvmArgumentBuilder.Sanitize(withoutHeap);
        var containedBlockedArguments =
            !string.Equals(sanitized, NewJavaArgument.Trim(), StringComparison.Ordinal);
        var additions = JavaArgumentTokenizer.Split(sanitized);

        if (additions.Count == 0)
        {
            JavaArgumentsError = _localizer["settings.javaSettings.jvmMemoryArgumentsManaged"];
            return;
        }

        foreach (var addition in additions)
            JavaArgumentItems.Add(new JavaArgumentItemViewModel(addition));

        UpdateJavaArgumentRows();
        PersistJavaArgumentItems();
        JavaArgumentsError = containedBlockedArguments
            ? _localizer["settings.javaSettings.jvmArgumentsBlocked"]
            : string.Empty;
        NewJavaArgument = string.Empty;
        IsAddingJavaArgument = false;
        ShowSaved();
    }

    [RelayCommand]
    private void RemoveJavaArgument(JavaArgumentItemViewModel? argument)
    {
        if (argument is null || !JavaArgumentItems.Remove(argument))
            return;

        UpdateJavaArgumentRows();
        PersistJavaArgumentItems();
        JavaArgumentsError = string.Empty;
        ShowSaved();
    }

    private void ReplaceJavaArgumentItems(string arguments)
    {
        JavaArgumentItems.Clear();
        foreach (var argument in JavaArgumentTokenizer.Split(arguments))
            JavaArgumentItems.Add(new JavaArgumentItemViewModel(argument));

        UpdateJavaArgumentRows();
    }

    private void UpdateJavaArgumentRows()
    {
        for (var index = 0; index < JavaArgumentItems.Count; index++)
            JavaArgumentItems[index].IsLast = index == JavaArgumentItems.Count - 1;

        OnPropertyChanged(nameof(HasJavaArguments));
        OnPropertyChanged(nameof(HasNoJavaArguments));
    }

    private void PersistJavaArgumentItems()
    {
        var arguments = JavaArgumentTokenizer.Join(JavaArgumentItems);
        _updatingJavaArguments = true;
        try
        {
            JavaArguments = arguments;
        }
        finally
        {
            _updatingJavaArguments = false;
        }

        _settings.JavaArguments = BuildPersistedJavaArguments(arguments);
    }

    [RelayCommand]
    private void ShowAddEnvironmentVariable()
    {
        NewEnvironmentVariable = string.Empty;
        EnvironmentVariablesError = string.Empty;
        IsAddingEnvironmentVariable = true;
    }

    [RelayCommand]
    private void CancelAddEnvironmentVariable()
    {
        NewEnvironmentVariable = string.Empty;
        IsAddingEnvironmentVariable = false;
    }

    private bool CanAddEnvironmentVariable()
        => !string.IsNullOrWhiteSpace(NewEnvironmentVariable);

    [RelayCommand(CanExecute = nameof(CanAddEnvironmentVariable))]
    private void AddEnvironmentVariable()
    {
        var variables = EnvironmentVariableParser.Parse(NewEnvironmentVariable);
        if (variables.Count == 0)
        {
            EnvironmentVariablesError = _localizer["settings.variablesSettings.envVarsInvalidFormat"];
            return;
        }

        foreach (var variable in variables)
            EnvironmentVariableItems.Add(new EnvironmentVariableItemViewModel(variable.Key, variable.Value));

        UpdateEnvironmentVariableRows();
        PersistEnvironmentVariableItems();
        EnvironmentVariablesError = string.Empty;
        NewEnvironmentVariable = string.Empty;
        IsAddingEnvironmentVariable = false;
        ShowSaved();
    }

    [RelayCommand]
    private void RemoveEnvironmentVariable(EnvironmentVariableItemViewModel? variable)
    {
        if (variable is null || !EnvironmentVariableItems.Remove(variable))
            return;

        UpdateEnvironmentVariableRows();
        PersistEnvironmentVariableItems();
        EnvironmentVariablesError = string.Empty;
        ShowSaved();
    }

    private void ReplaceEnvironmentVariableItems(string? variables)
    {
        EnvironmentVariableItems.Clear();
        foreach (var variable in EnvironmentVariableParser.Parse(variables))
            EnvironmentVariableItems.Add(new EnvironmentVariableItemViewModel(variable.Key, variable.Value));

        UpdateEnvironmentVariableRows();
    }

    private void UpdateEnvironmentVariableRows()
    {
        for (var index = 0; index < EnvironmentVariableItems.Count; index++)
            EnvironmentVariableItems[index].IsLast = index == EnvironmentVariableItems.Count - 1;

        OnPropertyChanged(nameof(HasEnvironmentVariables));
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
    }

    private void PersistEnvironmentVariableItems()
        => _settings.GameEnvironmentVariables = string.Join(
            '\n',
            EnvironmentVariableItems.Select(variable => $"{variable.Key}={variable.Value}"));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task BrowseInstanceFolder()
    {
        if (IsChangingInstanceFolder)
        {
            if (IsInstanceFolderChangeCancellationArmed)
                _instanceFolderChangeCancellation?.Cancel();

            return;
        }

        if (!CanChangeInstanceFolder || _filePicker is null)
            return;

        await RunInstanceFolderActionAsync(async cancellationToken =>
        {
            var selectedPath = await _filePicker.BrowseFolderAsync(InstanceFolder);
            if (!string.IsNullOrWhiteSpace(selectedPath))
                await MoveInstanceFolderAsync(selectedPath, cancellationToken);
        });
    }

    [RelayCommand]
    private Task ResetInstanceFolder()
        => CanChangeInstanceFolder && !IsDefaultInstanceFolder
            ? RunInstanceFolderActionAsync(
                cancellationToken => MoveInstanceFolderAsync(string.Empty, cancellationToken))
            : Task.CompletedTask;

    [RelayCommand]
    private Task OpenInstanceFolder()
        => _uriLauncher.LaunchDirectoryAsync(InstanceFolder);

    [RelayCommand]
    private Task OpenLauncherDataFolder()
        => _uriLauncher.LaunchDirectoryAsync(LauncherDataFolder);

    [RelayCommand] private Task OpenGitHub() => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism");
    [RelayCommand] private Task OpenDocumentation() => LaunchExternalAsync("https://hyprismteam.github.io/HyPrism/docs/");
    [RelayCommand] private Task OpenDiscord() => LaunchExternalAsync("https://discord.gg/hyprism");
    [RelayCommand] private Task OpenBugReport() => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism/issues/new/choose");
    [RelayCommand] private Task OpenLicense() => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism/blob/main/LICENSE");
    [RelayCommand] private Task OpenHytaleEula() => LaunchExternalAsync("https://hytale.com/eula");
    [RelayCommand] private Task OpenIcons8() => LaunchExternalAsync("https://icons8.com");
    [RelayCommand] private Task OpenLordicon() => LaunchExternalAsync("https://lordicon.com/");
    [RelayCommand] private Task OpenAvalonia() => LaunchExternalAsync("https://avaloniaui.net/");
    [RelayCommand] private Task OpenDotNet() => LaunchExternalAsync("https://dotnet.microsoft.com/");
    [RelayCommand]
    private Task OpenLatestCommit()
        => LaunchExternalAsync(_latestMainCommit?.HtmlUrl);

    [RelayCommand]
    private Task OpenTeamMember(AboutTeamMemberViewModel? member)
        => LaunchExternalAsync(member is null
            ? null
            : $"https://github.com/{member.GitHubLogin}");

    [RelayCommand]
    private Task OpenContributor(AboutContributorViewModel? contributor)
        => LaunchExternalAsync(contributor?.ProfileUrl);

    [RelayCommand]
    private Task OpenAllContributors()
        => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism/graphs/contributors");

    private Task LaunchExternalAsync(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? _uriLauncher.LaunchAsync(uri)
            : Task.FromResult(false);

    private async Task RunInstanceFolderActionAsync(Func<CancellationToken, Task> action)
    {
        using var cancellation = new CancellationTokenSource();
        var actionStartedAt = Stopwatch.GetTimestamp();
        var completedNormally = false;
        _instanceFolderChangeCancellation = cancellation;
        _isInstanceFolderChangeCancellationArmed = false;
        _canCancelInstanceFolderChange = false;
        IsMovingInstanceFolder = false;
        InstanceFolderChangeMetricText = string.Empty;
        IsChangingInstanceFolder = true;
        try
        {
            await action(cancellation.Token);
            completedNormally = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Info("Settings", "Instance folder change was cancelled");
        }
        finally
        {
            _canCancelInstanceFolderChange = false;
            _isInstanceFolderChangeCancellationArmed = false;
            OnPropertyChanged(nameof(IsInstanceFolderChangeCancellationArmed));
            if (completedNormally)
            {
                var remainingDuration = MinimumInstanceFolderActionDuration -
                    Stopwatch.GetElapsedTime(actionStartedAt);
                if (remainingDuration > TimeSpan.Zero)
                    await Task.Delay(remainingDuration);
            }

            if (ReferenceEquals(_instanceFolderChangeCancellation, cancellation))
                _instanceFolderChangeCancellation = null;

            IsMovingInstanceFolder = false;
            InstanceFolderChangeMetricText = string.Empty;
            IsChangingInstanceFolder = false;
        }
    }

    private async Task MoveInstanceFolderAsync(string path, CancellationToken cancellationToken)
    {
        _canCancelInstanceFolderChange = true;
        IsMovingInstanceFolder = true;
        var progress = new Progress<InstanceDirectoryMoveProgress>(moveProgress =>
        {
            if (!IsMovingInstanceFolder || cancellationToken.IsCancellationRequested)
                return;

            InstanceFolderChangeMetricText = moveProgress.TotalBytes > 0
                ? $"{moveProgress.Percentage}%"
                : string.Empty;
        });
        var previousDirectory = string.IsNullOrWhiteSpace(_settings.InstanceDirectory)
            ? _settings.DefaultInstanceDirectory
            : _settings.InstanceDirectory;
        if (!await _settings.SetInstanceDirectoryAsync(path, cancellationToken, progress))
            return;

        InstanceFolder = string.IsNullOrWhiteSpace(_settings.InstanceDirectory)
            ? _settings.DefaultInstanceDirectory
            : _settings.InstanceDirectory;
        RefreshStorageUsage();
        if (!Directory.Exists(previousDirectory))
            InvalidateCustomJavaPathUnder(previousDirectory);
    }

    private void InvalidateCustomJavaPathUnder(string previousDirectory)
    {
        if (!HasCustomJavaPath)
            return;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(previousDirectory)) +
                   Path.DirectorySeparatorChar;
        var javaPath = Path.GetFullPath(CustomJavaPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!javaPath.StartsWith(root, comparison))
            return;

        // The old instance tree is removed after a move, so a custom Java
        // living inside it can no longer resolve and must be re-picked
        CustomJavaPath = string.Empty;
        _settings.CustomJavaPath = string.Empty;
    }

    public void ArmInstanceFolderChangeCancellation()
    {
        if (!_canCancelInstanceFolderChange ||
            !IsMovingInstanceFolder ||
            _isInstanceFolderChangeCancellationArmed)
            return;

        _isInstanceFolderChangeCancellationArmed = true;
        OnPropertyChanged(nameof(IsInstanceFolderChangeCancellationArmed));
    }

    private void OnGameProcessStateChanged(object? sender, EventArgs args)
    {
        if (_gameProcess is null)
            return;

        Dispatcher.UIThread.Post(() => IsGameRunning = _gameProcess.IsGameRunning());
    }

    private void EnsureStorageUsageLoaded()
        => _ = EnsureStorageUsageLoadedAsync();

    private Task EnsureStorageUsageLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_storageUsageLoadStarted || _disposed)
            return _storageUsageLoadTask;

        _storageUsageLoadStarted = true;
        _storageUsageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _storageUsageLoadTask = LoadStorageUsageAsync(_storageUsageCancellation.Token);
        return _storageUsageLoadTask;
    }

    private void RefreshStorageUsage()
    {
        _storageUsageCancellation?.Cancel();
        _storageUsageCancellation?.Dispose();
        _storageUsageCancellation = null;
        _storageUsageLoadStarted = false;
        _storageUsageLoadTask = Task.CompletedTask;
        EnsureStorageUsageLoaded();
    }

    private async Task LoadStorageUsageAsync(CancellationToken cancellationToken)
    {
        var loaded = false;
        var showLoadingIndicator = _latestStorageUsage is null;
        if (showLoadingIndicator)
            IsStorageUsageLoading = true;
        try
        {
            var usage = await _settings.GetLauncherStorageUsageAsync(cancellationToken).ConfigureAwait(false);
            if (_disposed || cancellationToken.IsCancellationRequested || usage is null)
                return;

            await Dispatcher.UIThread.InvokeAsync(() => ApplyStorageUsage(usage));
            loaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Debug("Settings", "Storage usage calculation was cancelled");
        }
        catch (Exception exception)
        {
            Logger.Warning("Settings", $"Failed to calculate storage usage: {exception.Message}");
        }
        finally
        {
            if (!_disposed && _storageUsageCancellation?.Token == cancellationToken)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (showLoadingIndicator)
                        IsStorageUsageLoading = false;
                    _storageUsageCancellation?.Dispose();
                    _storageUsageCancellation = null;
                    if (!loaded)
                    {
                        _storageUsageLoadStarted = false;
                        _storageUsageLoadTask = Task.CompletedTask;
                    }
                });
            }
        }
    }

    private void ApplyStorageUsage(LauncherStorageUsage usage)
    {
        _latestStorageUsage = usage;
        TotalStorageUsage = FormatStorageSize(usage.TotalBytes);
        var total = Math.Max(1, usage.TotalBytes);
        var updatedItems = new[]
        {
            CreateStorageSegment(
                InstancesLabel,
                usage.InstanceBytes,
                total,
                "#245EA8",
                (_instanceRepository?.GetCachedInstances().Count ?? 0).ToString()),
            CreateStorageSegment(ImagesLabel, usage.ImageBytes, total, "#227F96"),
            CreateStorageSegment(ModsLabel, usage.ModBytes, total, "#60469B"),
            CreateStorageSegment(NewsLabel, usage.NewsBytes, total, "#A86416"),
            CreateStorageSegment(LogsLabel, usage.LogBytes, total, "#9C3A50"),
            CreateStorageSegment(OtherFilesLabel, usage.OtherBytes, total, "#197765")
        };
        if (!HasSameStorageUsageItems(updatedItems))
            StorageUsageItems = updatedItems;
    }

    private bool HasSameStorageUsageItems(IReadOnlyList<StorageUsageSegment> updatedItems)
    {
        if (StorageUsageItems.Count != updatedItems.Count)
            return false;

        for (var index = 0; index < updatedItems.Count; index++)
        {
            var current = StorageUsageItems[index];
            var updated = updatedItems[index];
            if (current.Label != updated.Label ||
                current.Bytes != updated.Bytes ||
                current.DisplaySize != updated.DisplaySize ||
                current.Percentage != updated.Percentage ||
                current.Count != updated.Count ||
                !AreStorageBrushesEqual(current.Brush, updated.Brush))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreStorageBrushesEqual(IBrush first, IBrush second)
        => first is ISolidColorBrush firstSolid &&
           second is ISolidColorBrush secondSolid
            ? firstSolid.Color == secondSolid.Color
            : ReferenceEquals(first, second);

    private static StorageUsageSegment CreateStorageSegment(
        string label,
        long bytes,
        long total,
        string color,
        string? count = null)
        => new(
            label,
            bytes,
            FormatStorageSize(bytes),
            $"{bytes * 100d / total:0.#}%",
            new SolidColorBrush(Color.Parse(color)),
            count);

    private void OnInstancesChanged()
    {
        if (_latestStorageUsage is null || _disposed)
            return;

        Dispatcher.UIThread.Post(() => ApplyStorageUsage(_latestStorageUsage));
    }

    private static string FormatStorageSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private static bool DirectoriesEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            comparison);
    }

    /// <summary>
    /// Warms repository metadata and avatar caches during launcher startup
    /// </summary>
    /// <param name="cancellationToken">Cancellation requested when the desktop exits</param>
    public Task PreloadAboutDataAsync(CancellationToken cancellationToken)
        => LoadAboutDataAsync().WaitAsync(cancellationToken);

    /// <summary>
    /// Calculates storage usage while the launcher startup screen is visible
    /// </summary>
    /// <param name="cancellationToken">Cancellation requested when startup ends or the desktop exits</param>
    public Task PreloadStorageUsageAsync(CancellationToken cancellationToken)
        => EnsureStorageUsageLoadedAsync(cancellationToken);

    private async Task LoadAboutDataAsync()
    {
        if (_gitHubClient is null || _aboutDataLoadStarted || _disposed)
            return;

        _aboutDataLoadStarted = true;
        IsAboutDataLoading = true;

        var contributorsTask = _gitHubClient.GetContributorsAsync();
        var commitTask = _gitHubClient.GetLatestMainCommitAsync();
        var teamAvatarTasks = AboutTeamMembers
            .Select(async member => new
            {
                Member = member,
                Avatar = await LoadGitHubAvatarAsync(
                    $"https://github.com/{Uri.EscapeDataString(member.GitHubLogin)}.png")
                    .ConfigureAwait(false)
            })
            .ToArray();

        var contributors = await contributorsTask.ConfigureAwait(false);
        var teamLogins = AboutTeamMembers
            .Select(member => member.GitHubLogin)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleContributors = contributors
            .Where(contributor => !teamLogins.Contains(contributor.Login) && !IsBot(contributor))
            .ToList();
        var contributorViewModels = visibleContributors
            .Select(contributor => new AboutContributorViewModel(
                contributor.Login,
                contributor.HtmlUrl,
                contributor.Contributions,
                contributor.AvatarUrl))
            .ToList();
        var contributorAvatarWarmup = WarmContributorAvatarCacheAsync(contributorViewModels);

        await Task.WhenAll(
                teamAvatarTasks.Cast<Task>().Append(contributorAvatarWarmup))
            .ConfigureAwait(false);
        var commit = await commitTask.ConfigureAwait(false);

        if (_disposed)
        {
            foreach (var contributor in contributorViewModels)
                contributor.Dispose();
            foreach (var result in teamAvatarTasks.Select(task => task.Result))
                result.Avatar?.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _latestMainCommit = commit;
            _aboutDataLoaded = true;
            HasAboutLatestCommit = commit is not null;
            AboutLatestCommitSha = commit is null
                ? string.Empty
                : commit.Sha[..Math.Min(7, commit.Sha.Length)];
            AboutLatestCommitHint = commit?.Message
                                    ?? _localizer["settings.aboutSettings.latestMainCommitUnavailable"];

            foreach (var result in teamAvatarTasks.Select(task => task.Result))
                result.Member.Avatar = result.Avatar;

            foreach (var contributor in _aboutContributorPool)
                contributor.Dispose();
            _aboutContributorPool.Clear();
            _aboutContributorPool.AddRange(contributorViewModels);
            RebuildVisibleAboutContributors();
            IsAboutDataLoading = false;
        });
    }

    /// <summary>
    /// Updates how many contributor circles fit in the available row
    /// </summary>
    /// <param name="slotCount">The number of circle slots available in the contributors container</param>
    public void UpdateAboutContributorCapacity(int slotCount)
    {
        var normalizedCapacity = Math.Max(1, slotCount);
        if (_aboutContributorSlotCapacity == normalizedCapacity)
            return;

        _aboutContributorSlotCapacity = normalizedCapacity;
        RebuildVisibleAboutContributors();
    }

    private void RebuildVisibleAboutContributors()
    {
        var visibleCount = _aboutContributorPool.Count > _aboutContributorSlotCapacity
            ? Math.Max(0, _aboutContributorSlotCapacity - 1)
            : _aboutContributorPool.Count;

        AboutContributors.Clear();
        foreach (var contributor in _aboutContributorPool.Take(visibleCount))
            AboutContributors.Add(contributor);

        var remainingCount = _aboutContributorPool.Count - visibleCount;
        HasMoreAboutContributors = remainingCount > 0;
        AboutContributorOverflow = $"+{remainingCount}";
        _ = LoadVisibleContributorAvatarsAsync();
    }

    private async Task LoadVisibleContributorAvatarsAsync()
    {
        if (_gitHubClient is null || _disposed)
            return;

        var contributors = AboutContributors
            .Where(contributor => !contributor.AvatarLoadStarted)
            .ToArray();
        foreach (var contributor in contributors)
            contributor.AvatarLoadStarted = true;

        var results = await Task.WhenAll(contributors.Select(async contributor => new
        {
            Contributor = contributor,
            Avatar = await LoadGitHubAvatarAsync(contributor.AvatarUrl).ConfigureAwait(false)
        })).ConfigureAwait(false);

        if (_disposed)
        {
            foreach (var result in results)
                result.Avatar?.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var result in results)
                result.Contributor.Avatar = result.Avatar;
        });
    }

    private async Task WarmContributorAvatarCacheAsync(
        IReadOnlyCollection<AboutContributorViewModel> contributors)
    {
        if (_gitHubClient is null)
            return;

        using var concurrencyGate = new SemaphoreSlim(4, 4);
        await Task.WhenAll(contributors.Select(async contributor =>
        {
            await concurrencyGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await _gitHubClient.LoadAvatarAsync(contributor.AvatarUrl, 96).ConfigureAwait(false);
            }
            finally
            {
                concurrencyGate.Release();
            }
        })).ConfigureAwait(false);
    }

    private async Task<Bitmap?> LoadGitHubAvatarAsync(string url)
    {
        try
        {
            var bytes = await _gitHubClient!.LoadAvatarAsync(url, 96).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                return null;

            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(stream, 96, BitmapInterpolationMode.HighQuality);
            }).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBot(GitHubUser contributor)
    {
        if (string.Equals(contributor.Type, "Bot", StringComparison.OrdinalIgnoreCase))
            return true;

        var login = contributor.Login;
        return BotLogins.Contains(login) ||
               login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
               login.EndsWith("-bot", StringComparison.OrdinalIgnoreCase) ||
               login.EndsWith("_bot", StringComparison.OrdinalIgnoreCase) ||
               login.EndsWith("bot[bot]", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowSaved() => StatusMessage = "✓";

    private void ReloadMirrorItems(bool clearStatus = true)
    {
        _downloadSourcesProbeStarted = false;
        if (clearStatus)
        {
            MirrorOperationError = string.Empty;
            MirrorOperationStatus = string.Empty;
        }

        MirrorSources.Clear();
        if (_mirrorCatalog is not null)
        {
            try
            {
                var mirrors = _mirrorCatalog.GetAll();
                for (var index = 0; index < mirrors.Count; index++)
                {
                    var mirror = mirrors[index];
                    MirrorSources.Add(new MirrorSourceViewModel(
                        mirror,
                        GetMirrorSourceType(mirror),
                        index == mirrors.Count - 1,
                        _localizer["settings.downloads.checkingAvailability"],
                        _localizer["settings.downloads.sourceDisabledState"],
                        PersistMirrorEnabledState));
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Settings", $"Failed to read download sources: {ex.Message}");
                MirrorOperationError = _localizer["settings.downloads.sourceReadFailed"];
            }
        }

        OnPropertyChanged(nameof(HasMirrors));
        OnPropertyChanged(nameof(HasNoMirrors));
        OfficialSourceAvailability = _localizer[_versionCatalog?.HasOfficialAccount == true
            ? "settings.downloads.checkingAvailability"
            : "settings.downloads.officialSourceRequiresAccount"];
        OfficialSourcePing = "—";
        OfficialSourceIsEnabled = _versionCatalog?.HasOfficialAccount == true;
        OfficialSourceIsChecking = _versionCatalog?.HasOfficialAccount == true;
        OfficialSourceIsAvailable = false;
        OfficialSourceIsUnavailable = _versionCatalog?.HasOfficialAccount != true;
        OnPropertyChanged(nameof(OfficialSourceAvailability));
        OnPropertyChanged(nameof(OfficialSourcePing));
        OnPropertyChanged(nameof(OfficialSourceIsEnabled));
        OnPropertyChanged(nameof(OfficialSourceIsChecking));
        OnPropertyChanged(nameof(OfficialSourceIsAvailable));
        OnPropertyChanged(nameof(OfficialSourceIsUnavailable));

        if (IsDownloads)
            EnsureDownloadSourcesProbed();
    }

    private void EnsureDownloadSourcesProbed()
    {
        if (_downloadSourcesProbeStarted)
            return;

        _downloadSourcesProbeStarted = true;
        _ = ProbeDownloadSourcesAsync();
    }

    private void EnsureAuthServersProbed()
    {
        if (!IsAuthServerVisible || _authServerProbeStarted)
            return;

        _authServerProbeStarted = true;
        _ = ProbeAuthServersAsync();
    }

    private async Task ProbeAuthServersAsync()
    {
        var cancellation = new CancellationTokenSource();
        var previousCancellation = _authServerProbeCancellation;
        _authServerProbeCancellation = cancellation;
        previousCancellation?.Cancel();
        var ct = cancellation.Token;

        foreach (var server in AuthServerItems)
            server.SetChecking(AuthServerCheckingLabel);

        try
        {
            await Task.WhenAll(AuthServerItems.Select(server => ProbeAuthServerAsync(server, ct)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.Debug("Settings", "Auth server probes were canceled");
        }
        finally
        {
            if (ReferenceEquals(_authServerProbeCancellation, cancellation))
                _authServerProbeCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ProbeAuthServerAsync(AuthServerItemViewModel server, CancellationToken ct)
    {
        try
        {
            var result = await CheckAuthServerAsync(server.Value, ct);
            if (!ct.IsCancellationRequested && AuthServerItems.Contains(server))
            {
                server.ApplyProbe(
                    result.IsAvailable,
                    result.PingMs,
                    AuthServerOnlineLabel,
                    AuthServerOfflineLabel,
                    AuthServerPingTemplate);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.Debug("Settings", $"Auth server probe was canceled for {server.Value}");
        }
        catch (Exception ex)
        {
            Logger.Debug("Settings", $"Auth server probe failed for {server.Value}: {ex.Message}");
            if (AuthServerItems.Contains(server))
            {
                server.ApplyProbe(
                    isAvailable: false,
                    pingMs: -1,
                    AuthServerOnlineLabel,
                    AuthServerOfflineLabel,
                    AuthServerPingTemplate);
            }
        }
    }

    private async Task<AuthServerProbeResult> CheckAuthServerAsync(
        string authServer,
        CancellationToken ct)
    {
        if (_httpClient is null)
            return new(false, -1);

        try
        {
            var result = await AuthServerAvailabilityChecker.CheckAsync(_httpClient, authServer, ct);
            return new(result.IsAvailable, result.PingMs);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, -1);
        }
        catch (Exception ex)
        {
            Logger.Debug("Settings", $"Auth server availability check failed for {authServer}: {ex.Message}");
            return new(false, -1);
        }
    }

    private void PersistMirrorEnabledState(MirrorSourceViewModel source)
    {
        if (_mirrorCatalog is null || _versionCatalog is null)
            return;

        try
        {
            _mirrorCatalog.Save(source.Definition);
            _versionCatalog.ReloadMirrorSources();
            MirrorOperationError = string.Empty;
            MirrorOperationStatus = source.IsEnabled
                ? _localizer["settings.downloads.sourceEnabled"]
                : _localizer["settings.downloads.sourceDisabled"];
            if (source.IsEnabled)
            {
                source.SetChecking(_localizer["settings.downloads.checkingAvailability"]);
                _ = ProbeMirrorAsync(source, CancellationToken.None);
            }
            else
            {
                source.SetDisabled(_localizer["settings.downloads.sourceDisabledState"]);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Settings", $"Failed to update download source: {ex.Message}");
            source.SetEnabledWithoutNotification(!source.IsEnabled);
            MirrorOperationError = _localizer["settings.downloads.sourceSaveFailed"];
        }
    }

    private string GetMirrorSourceType(Hyprism.Core.Models.MirrorMeta mirror)
        => _localizer[mirror.SourceType == "json-index"
            ? "settings.downloads.sourceTypeJsonIndex"
            : "settings.downloads.sourceTypePattern"];

    private string GetOfficialSourceAvailabilityLabel()
        => _localizer[OfficialSourceIsChecking
            ? "settings.downloads.checkingAvailability"
            : OfficialSourceIsAvailable
                ? "settings.downloads.sourceAvailable"
                : _versionCatalog?.HasOfficialAccount != true
                    ? "settings.downloads.officialSourceRequiresAccount"
                    : OfficialSourceIsUnavailable
                        ? "settings.downloads.sourceUnavailable"
                        : "settings.downloads.checkingAvailability"];

    private async Task ProbeDownloadSourcesAsync()
    {
        if (_versionCatalog is null)
            return;

        _sourceProbeCancellation?.Cancel();
        _sourceProbeCancellation?.Dispose();
        _sourceProbeCancellation = new CancellationTokenSource();
        var ct = _sourceProbeCancellation.Token;

        var probes = new List<Task>();
        if (_versionCatalog.HasOfficialAccount)
            probes.Add(ProbeOfficialSourceAsync(ct));
        foreach (var source in MirrorSources.Where(source => source.IsEnabled))
            probes.Add(ProbeMirrorAsync(source, ct));

        try
        {
            await Task.WhenAll(probes);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("Settings", "Download source probes were canceled");
        }
    }

    private async Task ProbeOfficialSourceAsync(CancellationToken ct)
    {
        try
        {
            var result = await _versionCatalog!.ProbeSourceAvailabilityAsync("hytale", ct);
            if (ct.IsCancellationRequested)
                return;

            OfficialSourceAvailability = _localizer[result.IsAvailable
                ? "settings.downloads.sourceAvailable"
                : "settings.downloads.sourceUnavailable"];
            OfficialSourcePing = result.IsAvailable && result.PingMs >= 0 ? $"{result.PingMs} ms" : "—";
            OfficialSourceIsChecking = false;
            OfficialSourceIsAvailable = result.IsAvailable;
            OfficialSourceIsUnavailable = !result.IsAvailable;
            OnPropertyChanged(nameof(OfficialSourceAvailability));
            OnPropertyChanged(nameof(OfficialSourcePing));
            OnPropertyChanged(nameof(OfficialSourceIsChecking));
            OnPropertyChanged(nameof(OfficialSourceIsAvailable));
            OnPropertyChanged(nameof(OfficialSourceIsUnavailable));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.Debug("Settings", "Official download source probe was canceled");
        }
        catch (Exception ex)
        {
            Logger.Debug("Settings", $"Official source probe failed: {ex.Message}");
            OfficialSourceAvailability = _localizer["settings.downloads.sourceUnavailable"];
            OfficialSourcePing = "—";
            OfficialSourceIsChecking = false;
            OfficialSourceIsAvailable = false;
            OfficialSourceIsUnavailable = true;
            OnPropertyChanged(nameof(OfficialSourceAvailability));
            OnPropertyChanged(nameof(OfficialSourcePing));
            OnPropertyChanged(nameof(OfficialSourceIsChecking));
            OnPropertyChanged(nameof(OfficialSourceIsAvailable));
            OnPropertyChanged(nameof(OfficialSourceIsUnavailable));
        }
    }

    private async Task ProbeMirrorAsync(MirrorSourceViewModel source, CancellationToken ct)
    {
        try
        {
            var result = await _versionCatalog!.ProbeSourceAvailabilityAsync(source.Id, ct);
            if (!ct.IsCancellationRequested && source.IsEnabled && MirrorSources.Contains(source))
            {
                source.ApplyProbe(
                    result,
                    _localizer["settings.downloads.sourceAvailable"],
                    _localizer["settings.downloads.sourceNoCompatibleVersions"],
                    _localizer["settings.downloads.sourceUnavailable"]);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.Debug("Settings", $"Download source probe was canceled for {source.Id}");
        }
        catch (Exception ex)
        {
            Logger.Debug("Settings", $"Download source probe failed for {source.Id}: {ex.Message}");
            if (source.IsEnabled && MirrorSources.Contains(source))
            {
                source.ApplyProbe(
                    new MirrorSpeedTestResult { IsAvailable = false, PingMs = -1 },
                    _localizer["settings.downloads.sourceAvailable"],
                    _localizer["settings.downloads.sourceNoCompatibleVersions"],
                    _localizer["settings.downloads.sourceUnavailable"]);
            }
        }
    }

    private static string NormalizeMirrorUrl(string value)
    {
        var normalized = value.Trim();
        return normalized.Contains("://", StringComparison.Ordinal)
            ? normalized
            : $"https://{normalized}";
    }

    private static bool IsAllowedMirrorUrl(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!))
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }

    private static bool TryGetMirrorEndpoint(MirrorMeta mirror, out Uri endpoint)
    {
        var value = mirror.SourceType switch
        {
            "pattern" => mirror.Pattern?.BaseUrl,
            "json-index" => mirror.JsonIndex?.ApiUrl,
            _ => null
        };

        return IsAllowedMirrorUrl(value ?? string.Empty, out endpoint);
    }

    private static bool EndpointsEqual(string existingEndpoint, Uri candidate)
    {
        if (!Uri.TryCreate(existingEndpoint, UriKind.Absolute, out var existing))
            return false;

        return string.Equals(existing.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(existing.Host, candidate.Host, StringComparison.OrdinalIgnoreCase) &&
               existing.Port == candidate.Port &&
               string.Equals(
                   existing.AbsolutePath.TrimEnd('/'),
                   candidate.AbsolutePath.TrimEnd('/'),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateUniqueMirrorId(string candidate, IEnumerable<string> existingIds)
    {
        var sanitized = new string(candidate
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '-')
            .ToArray())
            .Trim('-', '.', '_');
        if (sanitized.Length == 0)
            sanitized = "mirror";
        if (!char.IsAsciiLetterOrDigit(sanitized[0]))
            sanitized = $"mirror-{sanitized}";
        if (sanitized.Length > 56)
            sanitized = sanitized[..56].TrimEnd('-', '.', '_');

        var used = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(sanitized))
            return sanitized;

        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var unique = $"{sanitized}-{suffix}";
            if (!used.Contains(unique))
                return unique;
        }

        return $"mirror-{Guid.NewGuid():N}";
    }

    private void PersistJavaMemory()
    {
        if (_updatingJavaMemory)
            return;

        _updatingJavaMemory = true;
        try
        {
            var normalizedMaximum = NormalizeJavaMemory(JavaMaximumRamMb, MaximumJavaRamMb);
            var normalizedInitial = NormalizeJavaMemory(JavaInitialRamMb, normalizedMaximum);
            JavaMaximumRamMb = normalizedMaximum;
            JavaInitialRamMb = normalizedInitial;

            _settings.JavaArguments = BuildPersistedJavaArguments(JavaArguments);

            OnPropertyChanged(nameof(JavaMaximumRamValue));
            OnPropertyChanged(nameof(JavaInitialRamValue));
            OnPropertyChanged(nameof(JavaInitialRamMaximum));
        }
        finally
        {
            _updatingJavaMemory = false;
        }
    }

    private static double NormalizeJavaMemory(double value, double maximum)
    {
        var rounded = Math.Round(value / JavaMemoryStepMb, MidpointRounding.AwayFromZero) * JavaMemoryStepMb;
        return Math.Clamp(rounded, MinimumJavaMemoryMb, maximum);
    }

    private string BuildPersistedJavaArguments(string customArguments)
    {
        var updated = JvmArgumentBuilder.SetMaximumHeapMb(customArguments, (int)JavaMaximumRamMb);
        return JvmArgumentBuilder.SetInitialHeapMb(updated, (int)JavaInitialRamMb);
    }

    private static string FormatMemory(double memoryMb)
    {
        var memoryGb = memoryMb / 1024;
        return Math.Abs(memoryGb - Math.Round(memoryGb)) < 0.001
            ? $"{memoryGb:0} GB"
            : $"{memoryGb:0.#} GB";
    }

    private static ObservableCollection<SettingChoiceViewModel> CreateGpuPreferences(
        StringLocalizer localizer,
        IReadOnlyList<GpuAdapterInfo> adapters)
    {
        // The picker offers the detected cards themselves; the stored value is the
        // adapter key (pci:<id> when known, otherwise the adapter name)
        var preferences = adapters
            .Select(adapter => new SettingChoiceViewModel(GpuLaunchPreference.AdapterValue(adapter), adapter.Name))
            .ToList();
        preferences.Add(new("auto", localizer["settings.graphicsSettings.gpu_auto"]));
        return new ObservableCollection<SettingChoiceViewModel>(preferences);
    }

    private SettingChoiceViewModel ResolveGpuChoice(string? stored)
    {
        var normalized = stored?.Trim();
        var exact = GpuPreferences.FirstOrDefault(
            choice => string.Equals(choice.Value, normalized, StringComparison.Ordinal));
        if (exact is not null)
            return exact;

        // Legacy values from the old type-based preference map onto a detected card
        if (string.Equals(normalized, GpuLaunchPreference.Dedicated, StringComparison.OrdinalIgnoreCase))
            return FindChoiceForType(GpuLaunchPreference.Dedicated)
                ?? GpuPreferences[^1];

        if (string.Equals(normalized, GpuLaunchPreference.Integrated, StringComparison.OrdinalIgnoreCase))
            return FindChoiceForType(GpuLaunchPreference.Integrated)
                ?? GpuPreferences[^1];

        // A card that is no longer detected falls back to the discrete one, then to auto
        return FindChoiceForType(GpuLaunchPreference.Dedicated)
            ?? FindChoiceForType(GpuLaunchPreference.Integrated)
            ?? GpuPreferences[^1];
    }

    private SettingChoiceViewModel? FindChoiceForType(string type)
        => GpuPreferences.FirstOrDefault(IsChoiceForAdapterType(type));

    private Func<SettingChoiceViewModel, bool> IsChoiceForAdapterType(string type)
        => choice =>
        {
            var adapter = _detectedGpuAdapters.FirstOrDefault(
                candidate => string.Equals(GpuLaunchPreference.AdapterValue(candidate), choice.Value, StringComparison.Ordinal));
            return adapter is not null && string.Equals(adapter.Type, type, StringComparison.OrdinalIgnoreCase);
        };

    private static SettingChoiceViewModel FindChoice(
        IEnumerable<SettingChoiceViewModel> choices,
        string? value)
        => choices.FirstOrDefault(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
           ?? choices.First();

    private void UpdateCategoryLabel(string id, string label)
        => Categories.First(category => category.Id == id).Label = label;

    private void UpdateCategoryDescription(string id, string description)
        => Categories.First(category => category.Id == id).Description = description;

    private static void UpdateChoiceDisplay(
        IEnumerable<SettingChoiceViewModel> choices,
        string value,
        string display)
    {
        var choice = choices.FirstOrDefault(candidate => candidate.Value == value);
        if (choice is not null)
            choice.Display = display;
    }

    private static string GetFlagCountryCode(string cultureName)
    {
        var separatorIndex = cultureName.LastIndexOf('-');
        return separatorIndex >= 0
            ? cultureName[(separatorIndex + 1)..].ToUpperInvariant()
            : cultureName.ToUpperInvariant();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sourceProbeCancellation?.Cancel();
        _sourceProbeCancellation?.Dispose();
        _authServerProbeCancellation?.Cancel();
        _authServerAddCancellation?.Cancel();
        _storageUsageCancellation?.Cancel();
        _storageUsageCancellation?.Dispose();
        _instanceFolderChangeCancellation?.Cancel();
        if (_gameProcess is not null)
        {
            _gameProcess.GameProcessStarted -= OnGameProcessStateChanged;
            _gameProcess.GameProcessExited -= OnGameProcessStateChanged;
        }
        if (_instanceRepository is not null)
            _instanceRepository.InstancesChanged -= OnInstancesChanged;
        if (_profileRepository is not null)
            _profileRepository.ProfilesChanged -= OnProfilesChanged;
        foreach (var member in AboutTeamMembers)
            member.Dispose();
        foreach (var contributor in _aboutContributorPool)
            contributor.Dispose();
        _aboutContributorPool.Clear();
        AboutContributors.Clear();
    }
}

public enum DownloadSourceAdditionStep
{
    None,
    ChooseMethod,
    Automatic,
    Manual
}

internal readonly record struct AuthServerProbeResult(bool IsAvailable, long PingMs);

public sealed partial class JavaArgumentItemViewModel(string value) : ObservableObject
{
    public string Value { get; } = value;
    [ObservableProperty] private bool _isLast;
}

public sealed partial class EnvironmentVariableItemViewModel(string key, string value) : ObservableObject
{
    public string Key { get; } = key;
    public string Value { get; } = value;
    public string Display => $"{Key}={Value}";
    [ObservableProperty] private bool _isLast;
}

public sealed partial class AboutTeamMemberViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Creates a core team entry shown on the About page
    /// </summary>
    /// <param name="displayName">The name displayed in the launcher</param>
    /// <param name="initials">The fallback initials used before an avatar is available</param>
    /// <param name="roleKey">The localization key suffix for the team role</param>
    /// <param name="gitHubLogin">The GitHub login, or <c>null</c> when it matches the display name</param>
    public AboutTeamMemberViewModel(
        string displayName,
        string initials,
        string roleKey,
        string? gitHubLogin = null)
    {
        DisplayName = displayName;
        Initials = initials;
        RoleKey = roleKey;
        GitHubLogin = gitHubLogin ?? displayName;
    }

    public string DisplayName { get; }
    public string Initials { get; }
    public string RoleKey { get; }
    public string GitHubLogin { get; }
    public bool HasAvatar => Avatar is not null;
    [ObservableProperty] private string _role = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatar;

    partial void OnAvatarChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    /// <inheritdoc />
    public void Dispose()
        => Avatar = null;
}

/// <summary>
/// Represents a repository contributor displayed on the About page
/// </summary>
public sealed partial class AboutContributorViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Creates a contributor entry
    /// </summary>
    /// <param name="login">The GitHub login</param>
    /// <param name="profileUrl">The public GitHub profile URL</param>
    /// <param name="contributions">The contribution count reported by GitHub</param>
    /// <param name="avatarUrl">The GitHub avatar URL</param>
    public AboutContributorViewModel(
        string login,
        string profileUrl,
        int contributions,
        string avatarUrl)
    {
        Login = login;
        ProfileUrl = profileUrl;
        Contributions = contributions;
        AvatarUrl = avatarUrl;
    }

    public string Login { get; }
    public string ProfileUrl { get; }
    public int Contributions { get; }
    public string AvatarUrl { get; }
    public bool HasAvatar => Avatar is not null;
    public string Initial => string.IsNullOrWhiteSpace(Login) ? "?" : Login[..1].ToUpperInvariant();
    internal bool AvatarLoadStarted { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatar;

    partial void OnAvatarChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        Avatar = null;
    }
}

public sealed partial class SettingCategoryViewModel : ObservableObject
{
    public SettingCategoryViewModel(string id, string label, string icon)
    {
        Id = id;
        var iconUri = $"avares://Hyprism.Desktop/Assets/Fluent/{icon}";
        using var iconStream = AssetLoader.Open(new Uri(iconUri));
        Icon = new Bitmap(iconStream);
        _label = label;
    }

    public string Id { get; }
    public Bitmap Icon { get; }
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class SettingChoiceViewModel : ObservableObject
{
    public SettingChoiceViewModel(string value, string display, string? flagCountryCode = null)
    {
        Value = value;
        _display = display;

        if (!string.IsNullOrWhiteSpace(flagCountryCode))
        {
            var iconUri = $"avares://Hyprism.Desktop/Assets/Flags/{flagCountryCode}.png";
            using var iconStream = AssetLoader.Open(new Uri(iconUri));
            Icon = new Bitmap(iconStream);
        }
    }

    public string Value { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    [ObservableProperty] private string _display;
}
