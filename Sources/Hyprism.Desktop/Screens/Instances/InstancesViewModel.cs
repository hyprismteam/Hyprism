// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Game.Sources;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstancesViewModel : ObservableObject, IDisposable
{
    private const int ModCatalogPreviewFilesSkeletonMinMilliseconds = 220;
    private const int ModCatalogPreviewFilesFadeMilliseconds = 180;
    private const int MaxSavedInstanceIconDimension = 512;
    private static readonly TimeSpan InstanceVersionCacheMaxAge = TimeSpan.FromMinutes(15);

    private readonly IInstanceRepository _instances;
    private readonly IGameLaunchCoordinator _gameLaunchCoordinator;
    private readonly IGameInstallationWorkflow _installationWorkflow;
    private readonly IGameProcessTracker _gameProcess;
    private readonly IProgressReporter _progress;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly IFilePicker? _filePicker;
    private readonly IGameVersionCatalog? _versionCatalog;
    private readonly IModManager? _modManager;
    private readonly IGameConsoleService? _gameConsole;
    private readonly LogSessionPaths? _logSession;
    private readonly HttpClient _httpClient;
    private readonly RemoteImageCache? _remoteImageCache;
    private readonly StringLocalizer _localizer;
    private InstanceInfo? _selectedInstance;
    private InstanceInfo? _managedInstance;
    private bool _suppressInstancesChanged;
    private readonly Dictionary<string, int> _busyInstanceCounts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableRangeCollection<InstanceItemViewModel> _allInstances = [];
    private readonly ObservableRangeCollection<InstanceVersionItemViewModel> _availableInstanceVersions = [];
    private readonly ObservableRangeCollection<InstanceModItemViewModel> _installedMods = [];
    private readonly ObservableRangeCollection<InstanceModItemViewModel> _visibleInstalledMods = [];
    private readonly ObservableRangeCollection<ModCatalogItemViewModel> _modCatalogItems = [];
    private readonly ObservableRangeCollection<ModCatalogFileItemViewModel> _modCatalogPreviewFiles = [];
    private readonly ObservableRangeCollection<ModCatalogInstallItemViewModel> _modCatalogInstallItems = [];
    private readonly ObservableRangeCollection<InstanceWorldItemViewModel> _instanceWorlds = [];
    private ProgressUpdateMessage? _pendingProgressUpdate;
    private int _progressUpdateScheduled;
    private CancellationTokenSource? _instanceVersionsCancellation;
    private readonly DispatcherTimer _managedInstanceActionTimer;
    private DateTime? _managedInstanceActionStartedAtUtc;
    private DateTime? _managedInstanceGameStartedAtUtc;
    private string? _managedInstanceActionInstanceId;
    private long _managedInstanceActionGeneration;
    private readonly HashSet<long> _completedManagedActivityGenerations = [];
    private bool _managedInstanceActionStartedWithInstall;
    private bool _isManagedInstanceCancellationArmed;
    private string? _modsLoadedForInstanceId;
    private string? _worldsLoadedForInstanceId;
    private readonly object _pendingConsoleLock = new();
    private readonly List<GameConsoleLine> _pendingConsoleLines = [];
    private readonly HashSet<GameConsoleLine> _logsDisplayedLines = new(ReferenceEqualityComparer.Instance);
    private readonly ObservableRangeCollection<InstanceLogLineViewModel> _consoleLines = [];
    private string? _logsInstanceId;
    private CancellationTokenSource? _logsRebuildCancellation;
    private long _logsRebuildVersion;
    private int _consoleLinesVersion;
    private int _logsRebuildInProgress;
    private int _consoleFlushScheduled;
    private int _isDisposed;
    private readonly Dictionary<string, InstalledMod> _modUpdatesById = new(StringComparer.Ordinal);
    private bool _modCatalogFiltersLoaded;
    private bool _suppressCatalogReload;
    private List<ModCategory> _loadedModCategories = [];
    private int _modCatalogPage;
    private CancellationTokenSource _modIconsCancellation = new();
    private CancellationTokenSource _modDependencyIconsCancellation = new();
    private CancellationTokenSource _modPreviewImageCancellation = new();
    private CancellationTokenSource _modPreviewImageTransitionCancellation = new();
    private CancellationTokenSource _modPreviewRevealCancellation = new();
    private int _modCatalogPreviewVersion;
    private readonly List<Bitmap?> _modCatalogPreviewBitmaps = [];
    private readonly List<bool> _modCatalogPreviewBitmapSlotsResolved = [];
    private readonly object _modCatalogPreviewBitmapsLock = new();
    private bool _isModCatalogPreviewImageFadingOut;
    private bool _modCatalogInstallAccepted;
    private string? _modCatalogGameVersion;
    private const int ModCatalogPageSize = 24;
    private const int MaxConsoleLines = 3000;
    private const int SynchronousLogsRefreshLimit = 64;
    private const int LogsRenderBatchSize = 64;
    private static readonly TimeSpan LogsRenderBatchDelay = TimeSpan.FromMilliseconds(16);
    private static readonly IReadOnlyDictionary<string, string> ModCatalogCategoryResourceKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["blocks"] = "modManager.category.blocks",
            ["cosmetics-armor"] = "modManager.category.cosmetics_armor",
            ["food-farming"] = "modManager.category.food_farming",
            ["furniture"] = "modManager.category.furniture",
            ["gameplay"] = "modManager.category.gameplay",
            ["library"] = "modManager.category.library",
            ["miscellaneous"] = "modManager.category.miscellaneous",
            ["mobs-characters"] = "modManager.category.mobs_characters",
            ["prefab"] = "modManager.category.prefab",
            ["quality-of-life"] = "modManager.category.quality_of_life",
            ["qol"] = "modManager.category.quality_of_life",
            ["resource-packs"] = "modManager.category.resource_packs",
            ["utility"] = "modManager.category.utility",
            ["world-gen"] = "modManager.category.world_gen"
        };

    [ObservableProperty]
    private string _selectedInstanceName = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceMeta = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceState = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceBranch = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceVersion = string.Empty;

    [ObservableProperty]
    private string _selectedInstancePlayTime = string.Empty;

    [ObservableProperty]
    private string _managedInstanceName = string.Empty;

    [ObservableProperty]
    private string _managedInstanceState = string.Empty;

    [ObservableProperty]
    private string _managedInstanceBranch = string.Empty;

    [ObservableProperty]
    private string _managedInstanceVersion = string.Empty;

    [ObservableProperty]
    private string _managedInstancePlayTime = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManagedInstanceNotes))]
    private string _managedInstanceNotes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManagedInstanceIcon))]
    private Bitmap? _managedInstanceIcon;

    [ObservableProperty]
    private bool _isEditingInstance;

    [ObservableProperty]
    private bool _isInstanceEditMounted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingManagedInstanceDeletion))]
    [NotifyPropertyChangedFor(nameof(PendingManagedInstanceName))]
    [NotifyPropertyChangedFor(nameof(ManagedInstanceDeleteHint))]
    private InstanceInfo? _pendingManagedInstanceDeletion;

    [ObservableProperty]
    private bool _isManagedInstanceDeletionOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceDeletionError))]
    private string _instanceDeletionError = string.Empty;

    [ObservableProperty]
    private bool _isChoosingInstanceIcon;

    [ObservableProperty]
    private string _editInstanceName = string.Empty;

    [ObservableProperty]
    private string _editInstanceNotes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditInstanceIcon))]
    private Bitmap? _editInstanceIcon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceEditorError))]
    private string _instanceEditorError = string.Empty;

    private string? _editingInstanceId;
    private long _instanceIconPickerGeneration;
    private bool _removeInstanceIcon;
    private bool _replaceInstanceIcon;

    [ObservableProperty]
    private bool _canCancelActivity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunManagedInstanceAction))]
    [NotifyPropertyChangedFor(nameof(CanDeleteManagedInstance))]
    [NotifyPropertyChangedFor(nameof(CanEditManagedInstance))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunManagedInstanceAction))]
    [NotifyPropertyChangedFor(nameof(CanDeleteManagedInstance))]
    [NotifyPropertyChangedFor(nameof(CanEditManagedInstance))]
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _isActivityVisible;

    [ObservableProperty]
    private double _activityProgress;

    [ObservableProperty]
    private string _activityProgressText = "0%";

    [ObservableProperty]
    private string _activityTitle = string.Empty;

    [ObservableProperty]
    private string _activityDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateReleaseBranch))]
    [NotifyPropertyChangedFor(nameof(IsCreatePreReleaseBranch))]
    private string _newInstanceBranch = "release";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateInstance))]
    private bool _isInstanceVersionsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateInstance))]
    private InstanceVersionItemViewModel? _selectedNewInstanceVersion;

    [ObservableProperty]
    private bool _isInstanceCreatorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceCreationError))]
    private string _instanceCreationError = string.Empty;

    [ObservableProperty]
    private string _displayedInstanceSectionTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisplayedInstanceModsSection))]
    [NotifyPropertyChangedFor(nameof(IsDisplayedInstanceBrowseSection))]
    [NotifyPropertyChangedFor(nameof(IsDisplayedInstanceWorldsSection))]
    [NotifyPropertyChangedFor(nameof(IsDisplayedInstanceLogsSection))]
    private string _displayedInstanceSection = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstanceOverviewSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceModsSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceBrowseSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceWorldsSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceLogsSection))]
    [NotifyPropertyChangedFor(nameof(InstanceSectionTitle))]
    private string _instanceSection = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalledModsEmpty))]
    private bool _isInstanceModsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModCatalogEmpty))]
    [NotifyPropertyChangedFor(nameof(CanSearchModCatalog))]
    private bool _isModCatalogLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstanceWorldsEmpty))]
    private bool _isInstanceWorldsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceContentError))]
    private string _instanceContentError = string.Empty;

    [ObservableProperty]
    private string _installedModsSearchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShouldShowModCatalogSearchAction))]
    [NotifyPropertyChangedFor(nameof(CanSearchModCatalog))]
    private string _modCatalogSearchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedModCountText))]
    private int _selectedModCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModUpdates))]
    [NotifyPropertyChangedFor(nameof(ModUpdateCountText))]
    [NotifyPropertyChangedFor(nameof(InstanceModsUpdatesAvailableText))]
    private int _modUpdateCount;

    [ObservableProperty]
    private bool _isCheckingModUpdates;

    [ObservableProperty]
    private bool _isApplyingModUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLogsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasLogs))]
    private string _logsSearchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLogsAutoScroll = true;

    [ObservableProperty]
    private bool _isLogsWrap;

    [ObservableProperty]
    private bool _isLogsDebugEnabled = true;

    [ObservableProperty]
    private bool _isLogsWarningsEnabled = true;

    [ObservableProperty]
    private bool _isLogsErrorsEnabled = true;

    [ObservableProperty]
    private bool _isLogsTracingEnabled;

    [ObservableProperty]
    private bool _isLogsLevelPopupOpen;

    [ObservableProperty]
    private int _logsRevision;

    [ObservableProperty]
    private InstanceListOptionViewModel? _selectedModCatalogCategory;

    [ObservableProperty]
    private InstanceListOptionViewModel? _selectedModCatalogSort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreModCatalog))]
    private bool _isLoadingMoreModCatalog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreModCatalog))]
    private bool _hasMoreModCatalog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallModCatalogPreview))]
    [NotifyPropertyChangedFor(nameof(HasMultipleModCatalogPreviewScreenshots))]
    [NotifyPropertyChangedFor(nameof(CanShowPreviousModCatalogScreenshot))]
    [NotifyPropertyChangedFor(nameof(CanShowNextModCatalogScreenshot))]
    [NotifyPropertyChangedFor(nameof(IsModCatalogPreviewMounted))]
    [NotifyPropertyChangedFor(nameof(IsBottomSheetMounted))]
    private ModCatalogItemViewModel? _selectedModCatalogPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModCatalogPreview))]
    private bool _isModCatalogPreviewOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallModCatalogPreview))]
    private ModCatalogFileItemViewModel? _selectedModCatalogPreviewFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModCatalogPreviewImage))]
    private Bitmap? _modCatalogPreviewImage;

    [ObservableProperty]
    private bool _isModCatalogPreviewLoading;

    [ObservableProperty]
    private bool _isModCatalogPreviewFilesSkeletonVisible;

    [ObservableProperty]
    private bool _isModCatalogPreviewFilesSkeletonFadingOut;

    [ObservableProperty]
    private bool _isModCatalogPreviewFilesContentVisible;

    [ObservableProperty]
    private bool _isModCatalogPreviewImageLoading;

    [ObservableProperty]
    private bool _isModCatalogPreviewImageVisible;

    [ObservableProperty]
    private bool _isModCatalogPreviewImageTransitioning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallSelectedCatalogMods))]
    [NotifyPropertyChangedFor(nameof(CanOpenModCatalogInstallConfirmation))]
    private bool _isInstallingSelectedCatalogMods;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModCatalogInstallConfirmation))]
    [NotifyPropertyChangedFor(nameof(IsBottomSheetMounted))]
    private bool _isModCatalogInstallConfirmationOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModCatalogInstallProgressText))]
    private double _modCatalogInstallProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModCatalogInstallProgressText))]
    private int _modCatalogInstallCompletedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowPreviousModCatalogScreenshot))]
    [NotifyPropertyChangedFor(nameof(CanShowNextModCatalogScreenshot))]
    private int _modCatalogPreviewScreenshotIndex;


    public InstancesViewModel(
        IInstanceRepository instances,
        IGameLaunchCoordinator gameLaunchCoordinator,
        IGameInstallationWorkflow installationWorkflow,
        IGameProcessTracker gameProcess,
        IProgressReporter progress,
        IExternalUriLauncher uriLauncher,
        HttpClient httpClient,
        StringLocalizer localizer,
        IFilePicker? filePicker = null,
        IGameVersionCatalog? versionCatalog = null,
        IModManager? modManager = null,
        RemoteImageCache? remoteImageCache = null,
        IGameConsoleService? gameConsole = null,
        LogSessionPaths? logSession = null)
    {
        _instances = instances;
        _gameLaunchCoordinator = gameLaunchCoordinator;
        _installationWorkflow = installationWorkflow;
        _gameProcess = gameProcess;
        _progress = progress;
        _uriLauncher = uriLauncher;
        _filePicker = filePicker;
        _versionCatalog = versionCatalog;
        _modManager = modManager;
        _gameConsole = gameConsole;
        _logSession = logSession;
        _httpClient = httpClient;
        _remoteImageCache = remoteImageCache;
        _localizer = localizer;
        _localizer.LanguageChanged += ApplyLanguage;

        _progress.DownloadProgressChanged += OnDownloadProgressChanged;
        _progress.OperationErrorOccurred += OnOperationErrorOccurred;
        _gameProcess.GameProcessStarted += OnGameProcessStarted;
        _gameProcess.GameProcessExited += OnGameProcessExited;
        _gameLaunchCoordinator.LaunchFailed += OnLaunchFailed;
        _instances.InstancesChanged += OnInstancesChanged;
        IsGameRunning = _gameProcess.IsGameRunning();

        _managedInstanceActionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _managedInstanceActionTimer.Tick += OnManagedInstanceActionTimerTick;

        if (_gameConsole is not null)
            _gameConsole.LineReceived += OnConsoleLineReceived;
        BuildModCatalogSortOptions();

        RefreshInstances();
        RefreshManagedInstanceContent();
        if (IsGameRunning)
            _managedInstanceActionTimer.Start();
    }

    public ObservableCollection<InstanceItemViewModel> AllInstances => _allInstances;
    public ObservableCollection<InstanceVersionItemViewModel> AvailableInstanceVersions => _availableInstanceVersions;
    public ObservableCollection<InstanceModItemViewModel> InstalledMods => _installedMods;
    public ObservableCollection<InstanceModItemViewModel> VisibleInstalledMods => _visibleInstalledMods;
    public ObservableCollection<ModCatalogItemViewModel> ModCatalogItems => _modCatalogItems;
    public ObservableCollection<ModCatalogFileItemViewModel> ModCatalogPreviewFiles => _modCatalogPreviewFiles;
    public ObservableCollection<ModCatalogInstallItemViewModel> ModCatalogInstallItems => _modCatalogInstallItems;
    public ObservableCollection<InstanceWorldItemViewModel> InstanceWorlds => _instanceWorlds;
    public ObservableCollection<InstanceLogLineViewModel> LogsLines => _consoleLines;
    public ObservableCollection<InstanceListOptionViewModel> ModCatalogCategories { get; } = [];
    public ObservableCollection<InstanceListOptionViewModel> ModCatalogSortOptions { get; } = [];

    public string SelectInstanceLabel => _localizer["main.selectInstance"];
    public string VersionLabel => _localizer["common.version"];
    public string BranchLabel => _localizer["common.branch"];
    public string ReleaseLabel => _localizer["common.release"];
    public string PreReleaseLabel => _localizer["common.preRelease"];
    public string InstancesSectionLabel => _localizer["instances.title"];
    public string SelectVersionLabel => _localizer["instances.selectVersion"];
    public string CreateInstanceLabel => _localizer["instances.create"];
    public string CreateInstanceTitle => _localizer["instances.createInstance"];
    public string NewInstanceTitle => _localizer["instances.newInstance"];
    public string NewInstanceHint => _localizer["instances.newInstanceHint"];
    public string CreateInstanceHint => _localizer["instances.createInstanceHint"];
    public string InstanceBranchHint => _localizer["instances.branchHint"];
    public string InstanceVersionHint => _localizer["instances.versionHint"];
    public string CancelLabel => _localizer["common.cancel"];
    public string BackLabel => _localizer["common.back"];
    public string InstanceModsLabel => _localizer["instances.tab.mods"];
    public string InstanceBrowseLabel => _localizer["instances.tab.browse"];
    public string InstanceWorldsLabel => _localizer["instances.tab.worlds"];
    public string InstanceModsTitle => _localizer["instances.mods.title"];
    public string InstanceModsHint => _localizer["instances.mods.hint"];
    public string InstanceModsSearchHint => _localizer["instances.mods.search"];
    public string InstanceModsEmptyTitle => _localizer["instances.mods.emptyTitle"];
    public string InstanceModsEmptyHint => _localizer["instances.mods.emptyHint"];
    public string InstanceBrowseTitle => _localizer["instances.browse.title"];
    public string InstanceBrowseHint => _localizer["instances.browse.hint"];
    public string InstanceBrowseSearchHint => _localizer["instances.browse.search"];
    public string InstanceBrowseEmptyTitle => _localizer["instances.browse.emptyTitle"];
    public string InstanceBrowseEmptyHint => _localizer["instances.browse.emptyHint"];
    public string InstanceWorldsTitle => _localizer["instances.worlds.title"];
    public string InstanceWorldsHint => _localizer["instances.worlds.hint"];
    public string InstanceWorldsEmptyTitle => _localizer["instances.worlds.emptyTitle"];
    public string InstanceWorldsEmptyHint => _localizer["instances.worlds.emptyHint"];
    public string InstanceLogsTitle => _localizer["instances.logs.title"];
    public string InstanceLogsHint => _localizer["instances.logs.hint"];
    public string InstanceLogsEmptyTitle => _localizer["instances.logs.emptyTitle"];
    public string InstanceLogsEmptyHint => _localizer["instances.logs.emptyHint"];
    public string InstanceModsCheckUpdatesLabel => _localizer["instances.mods.checkUpdates"];
    public string InstanceModsUpdateAllLabel => _localizer["instances.mods.updateAll"];
    public string InstanceModsAddLabel => _localizer["instances.mods.add"];
    public string InstanceModsDropImportLabel => _localizer["instances.mods.dropToImport"];
    public string InstanceModsSelectAllLabel => _localizer["instances.mods.selectAll"];
    public string InstanceModsClearSelectionLabel => _localizer["common.clear"];
    public string InstanceModsEnableSelectedLabel => _localizer["instances.mods.enableSelected"];
    public string InstanceModsDisableSelectedLabel => _localizer["instances.mods.disableSelected"];
    public string InstanceModsDeleteSelectedLabel => _localizer["instances.mods.deleteSelected"];
    public string InstanceModsDeleteTooltip => _localizer["common.delete"];
    public string InstanceModsDeleteLabel => _localizer["common.delete"];
    public string InstanceModsDeleteTitle => _localizer["instances.mods.deleteTitle"];
    public string InstanceModsDeleteHint => _localizer["instances.mods.deleteHint"];
    public string InstanceModsOpenPageTooltip => _localizer["instances.mods.openPage"];
    public string InstanceModsToggleTooltip => _localizer["instances.mods.toggle"];
    public string InstanceBrowseCategoryLabel => _localizer["instances.browse.category"];
    public string InstanceBrowseSortLabel => _localizer["instances.browse.sort"];
    public string InstanceBrowseLoadingMoreLabel => _localizer["instances.browse.loadingMore"];
    public string ModCatalogPreviewAuthorLabel => _localizer["modManager.author"];
    public string ModCatalogPreviewDownloadsLabel => _localizer["modManager.downloads"];
    public string ModCatalogPreviewNoFilesLabel => _localizer["modManager.noFilesAvailable"];
    public string ModCatalogPreviewCloseLabel => _localizer["common.close"];
    public string ModCatalogOpenCurseForgeLabel => _localizer["modManager.openCurseforge"];
    public string ModCatalogFileTypeColumn => _localizer["settings.downloads.columnType"];
    public string ModCatalogFileNameColumn => _localizer["modManager.name"];
    public string ModCatalogFileGameVersionsColumn => _localizer["modManager.gameVersions"];
    public string ModCatalogInstallSelectedLabel => InstallLabel;
    public string ModCatalogInstallTitle => _localizer["modManager.installSelected"];
    public string ModCatalogInstallPreviewTitle => _localizer["modManager.installPreview"];
    public string ModCatalogInstallDependencyHint => _localizer["modManager.installDependencyHint"];
    public string ModCatalogInstallVersionColumn => VersionLabel;
    public string ModCatalogInstallDependenciesColumn => _localizer["modManager.dependencies"];
    public string ModCatalogGameVersionLabel => string.IsNullOrWhiteSpace(_modCatalogGameVersion)
        ? _localizer["instances.mods.compatibility.versionUnknown"]
        : _localizer.Format("instances.mods.compatibility.gameVersion", _modCatalogGameVersion);
    public string LogsAutoScrollLabel => _localizer["instances.logs.autoScroll"];
    public string LogsClearLabel => _localizer["instances.logs.clear"];
    public string LogsSearchHint => _localizer["instances.logs.search"];
    public string LogsWrapLabel => _localizer["instances.logs.wrap"];
    public string LogsLevelLabel => _localizer["instances.logs.level"];
    public string LogsDebugLabel => _localizer["instances.logs.levelDebug"];
    public string LogsWarningsLabel => _localizer["instances.logs.levelWarnings"];
    public string LogsErrorsLabel => _localizer["instances.logs.levelErrors"];
    public string LogsTracingLabel => _localizer["instances.logs.levelTracing"];
    public string LogsShowInFolderLabel => _localizer["instances.logs.showInFolder"];
    public int SelectedLogsLevelCount =>
        (IsLogsDebugEnabled ? 1 : 0) +
        (IsLogsWarningsEnabled ? 1 : 0) +
        (IsLogsErrorsEnabled ? 1 : 0) +
        (IsLogsTracingEnabled ? 1 : 0);
    public string LogsLevelSummary =>
        IsLogsDebugEnabled ? LogsDebugLabel :
        IsLogsWarningsEnabled ? LogsWarningsLabel :
        IsLogsErrorsEnabled ? LogsErrorsLabel :
        IsLogsTracingEnabled ? LogsTracingLabel : LogsLevelLabel;
    public bool HasAdditionalLogsLevels => SelectedLogsLevelCount > 1;
    public string LogsAdditionalLevelCountText => $"+{Math.Max(0, SelectedLogsLevelCount - 1)}";
    public bool CanShowLogsInFolder => _logSession is not null &&
        _managedInstance is { } instance &&
        File.Exists(_logSession.GetInstanceLogPath(instance.Id));

    public bool HasModSelection => SelectedModCount > 0;
    public bool HasModUpdates => ModUpdateCount > 0;
    public bool HasLogs => LogsLines.Count > 0;
    public bool IsLogsEmpty => LogsLines.Count == 0;
    public bool CanLoadMoreModCatalog => HasMoreModCatalog && !IsLoadingMoreModCatalog && !IsModCatalogLoading;
    public bool ShouldShowModCatalogSearchAction => ModCatalogSearchQuery.Trim().Length > 3;
    public bool CanSearchModCatalog => ShouldShowModCatalogSearchAction && !IsModCatalogLoading;
    public bool HasModCatalogPreview => IsModCatalogPreviewOpen;
    public bool IsModCatalogPreviewMounted => SelectedModCatalogPreview is not null;
    public bool IsBottomSheetMounted => IsModCatalogPreviewMounted || HasModCatalogInstallConfirmation;
    public bool HasModCatalogInstallConfirmation => IsModCatalogInstallConfirmationOpen;
    public bool HasModCatalogPreviewImage => ModCatalogPreviewImage is not null;
    public bool HasModCatalogPreviewFiles => ModCatalogPreviewFiles.Count > 0;
    public bool HasMultipleModCatalogPreviewScreenshots =>
        SelectedModCatalogPreview is { ScreenshotUrls.Count: > 1 };
    public bool CanShowPreviousModCatalogScreenshot =>
        ModCatalogPreviewScreenshotIndex > 0;
    public bool CanShowNextModCatalogScreenshot =>
        SelectedModCatalogPreview is { } item &&
        ModCatalogPreviewScreenshotIndex < item.ScreenshotUrls.Count - 1;
    public bool CanInstallModCatalogPreview =>
        SelectedModCatalogPreview is { IsInstalling: false } &&
        SelectedModCatalogPreviewFile is { CanInstall: true };
    public int SelectedCatalogModCount => ModCatalogItems.Count(item => item.IsSelected);
    public bool HasSelectedCatalogMods => SelectedCatalogModCount > 0;
    public bool CanInstallSelectedCatalogMods => HasSelectedCatalogMods && !IsInstallingSelectedCatalogMods;
    public bool CanOpenModCatalogInstallConfirmation => HasSelectedCatalogMods && !IsInstallingSelectedCatalogMods;
    public string ModCatalogInstallProgressText =>
        $"{ModCatalogInstallCompletedCount}/{ModCatalogInstallItems.Count}";
    public string SelectedModCountText =>
        _localizer.Format("instances.mods.selectedCount", SelectedModCount);
    public string ModUpdateCountText =>
        ModUpdateCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InstanceModsUpdatesAvailableText =>
        _localizer.Format("instances.mods.updatesAvailable", ModUpdateCount);
    public string InstanceModsFooterText =>
        _localizer.Format("instances.mods.countInstalled", InstalledMods.Count);
    public string InstanceNotInstalledTitle => _localizer["instances.content.notInstalledTitle"];
    public string InstanceNotInstalledHint => _localizer["instances.content.notInstalledHint"];
    public string RefreshLabel => _localizer["common.refresh"];
    public string InstallLabel => _localizer["instances.mods.install"];
    public string InstalledLabel => _localizer["instances.mods.installed"];
    public string EnabledLabel => _localizer["instances.mods.enabled"];
    public string DisabledLabel => _localizer["instances.mods.disabled"];
    public string InstanceContentBackLabel => _localizer["instances.content.back"];
    public string ManagedInstancePlayLabel => _localizer["instances.actions.play"];
    public string ManagedInstanceInstallLabel => _localizer["instances.actions.install"];
    public string ManagedInstanceOpenFolderLabel => _localizer["instances.actions.openFolder"];
    public string EditInstanceLabel => _localizer["editor.action"];
    public string EditInstanceTitle => _localizer["instances.editInstance"];
    public string EditInstanceNameLabel => _localizer["instances.instanceName"];
    public string EditInstanceNamePlaceholder => _localizer["instances.instanceNamePlaceholder"];
    public string EditInstanceNotesLabel => _localizer["instances.editor.description"];
    public string EditInstanceIconTitle => _localizer["instances.editor.icon"];
    public string EditInstanceIconLabel => _localizer["instances.selectIcon"];
    public string ResetInstanceIconLabel => _localizer["common.reset"];
    public string SaveInstanceLabel => _localizer["common.save"];
    public string CancelInstanceEditLabel => _localizer["common.cancel"];
    public bool CanChooseInstanceIcon => _filePicker is not null;
    public bool HasManagedInstanceIcon => ManagedInstanceIcon is not null;
    public bool HasManagedInstanceNotes => !string.IsNullOrWhiteSpace(ManagedInstanceNotes);
    public bool HasEditInstanceIcon => EditInstanceIcon is not null;
    public bool HasInstanceEditorError => !string.IsNullOrEmpty(InstanceEditorError);
    public bool HasPendingManagedInstanceDeletion => PendingManagedInstanceDeletion is not null;
    public bool HasInstanceDeletionError => !string.IsNullOrEmpty(InstanceDeletionError);
    public string PendingManagedInstanceName => PendingManagedInstanceDeletion is { } instance
        ? FormatInstanceName(instance.Name, instance.Version, instance.VersionName)
        : string.Empty;
    public string ManagedInstanceDeleteLabel => _localizer["instances.actions.delete"];
    public string ManagedInstanceDeleteTitle => _localizer["confirmation.title"];
    public string ManagedInstanceDeleteHint =>
        _localizer.Format("instances.actions.deleteHint", PendingManagedInstanceName);
    public string ManagedInstanceActionLabel =>
        IsManagedInstanceInstalled ? ManagedInstancePlayLabel : ManagedInstanceInstallLabel;
    public string ManagedInstanceActionCancelLabel => _localizer["instances.actions.cancel"];
    public string ManagedInstanceActionStatusText => IsManagedInstanceActionRunning
        ? _localizer["instances.actions.running"]
        : _managedInstanceActionStartedWithInstall
            ? ActivityTitle
            : _localizer["instances.actions.launching"];
    public string ManagedInstanceActionMetricText => _managedInstanceActionStartedWithInstall &&
                                                     !IsManagedInstanceActionRunning
        ? ActivityProgressText
        : FormatManagedInstanceActionElapsedTime();
    public string InstanceStatusInfoLabel => _localizer["instances.info.status"];
    public string InstancePlayTimeInfoLabel => _localizer["instances.info.playtime"];
    public string InstanceModsInfoLabel => _localizer["instances.info.mods"];
    public string InstanceWorldsInfoLabel => _localizer["instances.info.worlds"];
    public string InstanceModsCountText => InstalledMods.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InstanceWorldsCountText => InstanceWorlds.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InstanceSectionTitle => GetInstanceSectionTitle(InstanceSection);

    private string GetInstanceSectionTitle(string section) => section switch
    {
        "mods" => InstanceModsTitle,
        "browse" => InstanceBrowseTitle,
        "worlds" => InstanceWorldsTitle,
        "logs" => InstanceLogsTitle,
        _ => ManagedInstanceName
    };

    public bool HasInstances => AllInstances.Count > 0;
    public bool HasSelectedInstance => _selectedInstance is not null;
    public bool HasManagedInstance => _managedInstance is not null;
    public bool HasAvailableInstanceVersions => AvailableInstanceVersions.Count > 0;
    public bool HasInstanceCreationError => !string.IsNullOrWhiteSpace(InstanceCreationError);
    public bool HasInstanceContentError => !string.IsNullOrWhiteSpace(InstanceContentError);
    public bool IsSelectedInstanceInstalled => _selectedInstance?.IsInstalled == true;
    public bool IsManagedInstanceInstalled => _managedInstance?.IsInstalled == true;
    public bool IsManagedInstanceActionActive => IsManagedInstanceRunning ||
        (_managedInstance is not null &&
         string.Equals(
             _managedInstance.Id,
             _managedInstanceActionInstanceId,
             StringComparison.OrdinalIgnoreCase));
    public bool IsManagedInstanceActionRunning => IsManagedInstanceRunning;
    public bool ShouldSpinManagedInstanceAction =>
        IsManagedInstanceActionActive && !IsManagedInstanceActionRunning;
    private bool IsManagedInstanceRunning => _managedInstance is not null
        && _gameProcess.IsInstanceRunning(_managedInstance.Id);
    public bool IsManagedInstanceCancellationArmed =>
        IsManagedInstanceActionActive && _isManagedInstanceCancellationArmed;
    public bool CanRunManagedInstanceAction =>
        _managedInstance is not null &&
        ((!IsInstanceBusy(_managedInstance.Id) && !IsManagedInstanceRunning) || IsManagedInstanceActionActive);
    public bool CanOpenManagedInstanceFolder => _managedInstance is not null;
    public bool CanDeleteManagedInstance =>
        _managedInstance is not null && !IsInstanceBusy(_managedInstance.Id) && !IsManagedInstanceRunning;
    public bool CanEditManagedInstance => _managedInstance is not null &&
        (!IsInstanceBusy(_managedInstance.Id) || IsManagedInstanceRunning);
    public bool IsManagedInstanceEditActionCollapsed =>
        IsManagedInstanceActionActive && !IsManagedInstanceRunning;
    public bool IsInstanceOverviewSection => string.IsNullOrEmpty(InstanceSection);
    public bool IsInstanceModsSection => InstanceSection == "mods";
    public bool IsInstanceBrowseSection => InstanceSection == "browse";
    public bool IsInstanceWorldsSection => InstanceSection == "worlds";
    public bool IsInstanceLogsSection => InstanceSection == "logs";
    public bool IsDisplayedInstanceModsSection => DisplayedInstanceSection == "mods";
    public bool IsDisplayedInstanceBrowseSection => DisplayedInstanceSection == "browse";
    public bool IsDisplayedInstanceWorldsSection => DisplayedInstanceSection == "worlds";
    public bool IsDisplayedInstanceLogsSection => DisplayedInstanceSection == "logs";
    public bool HasInstalledMods => VisibleInstalledMods.Count > 0;
    public bool HasModCatalogItems => ModCatalogItems.Count > 0;
    public bool HasInstanceWorlds => InstanceWorlds.Count > 0;
    public bool IsInstalledModsEmpty =>
        IsManagedInstanceInstalled && !IsInstanceModsLoading && !HasInstalledMods;
    public bool IsModCatalogEmpty =>
        IsManagedInstanceInstalled && !IsModCatalogLoading && !HasModCatalogItems;
    public bool IsInstanceWorldsEmpty =>
        IsManagedInstanceInstalled && !IsInstanceWorldsLoading && !HasInstanceWorlds;
    public bool CanCreateInstance => !IsInstanceVersionsLoading && SelectedNewInstanceVersion is not null;
    public bool IsCreateReleaseBranch =>
        string.Equals(NewInstanceBranch, "release", StringComparison.OrdinalIgnoreCase);
    public bool IsCreatePreReleaseBranch => !IsCreateReleaseBranch;

    private void ApplyLanguage(string language)
        => RefreshLocalization();

    public void RefreshLocalization()
    {
        if (_loadedModCategories.Count > 0)
            RebuildModCatalogCategories();
        BuildModCatalogSortOptions();
        NotifyLogsStateChanged();

        if (!IsInstanceOverviewSection)
            DisplayedInstanceSectionTitle = InstanceSectionTitle;
        UpdateSelectedInstancePresentation();
        UpdateManagedInstancePresentation();
        OnPropertyChanged(string.Empty);
    }


    public void MoveInstance(string instanceId, int targetIndex)
    {
        var sourceIndex = -1;
        for (var index = 0; index < AllInstances.Count; index++)
        {
            if (string.Equals(AllInstances[index].Id, instanceId, StringComparison.Ordinal))
            {
                sourceIndex = index;
                break;
            }
        }

        if (sourceIndex < 0 || AllInstances.Count < 2)
            return;

        targetIndex = Math.Clamp(targetIndex, 0, AllInstances.Count - 1);
        if (sourceIndex == targetIndex)
            return;

        AllInstances.Move(sourceIndex, targetIndex);
        _instances.SetInstanceOrder(AllInstances.Select(instance => instance.Id).ToArray());
    }
    [RelayCommand]
    private void OpenInstanceCreator()
    {
        IsInstanceCreatorOpen = true;
        InstanceCreationError = string.Empty;
        _ = LoadInstanceVersionsAsync(NewInstanceBranch);
    }

    [RelayCommand]
    private void CloseInstanceCreator()
    {
        IsInstanceCreatorOpen = false;
        ResetInstanceCreatorState();
    }

    [RelayCommand]
    private void SetNewInstanceBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) ||
            string.Equals(NewInstanceBranch, branch, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        NewInstanceBranch = branch;
        InstanceCreationError = string.Empty;
        _ = LoadInstanceVersionsAsync(branch);
    }

    [RelayCommand]
    private void SelectNewInstanceVersion(InstanceVersionItemViewModel? version)
    {
        if (version is null || version.Version == SelectedNewInstanceVersion?.Version)
            return;

        SelectedNewInstanceVersion = version;
        RefreshAvailableInstanceVersionSelection(version.Version);
    }

    [RelayCommand]
    private void CreateInstance()
    {
        if (IsInstanceVersionsLoading || SelectedNewInstanceVersion is null)
            return;

        try
        {
            var version = SelectedNewInstanceVersion.Version;
            var branch = NewInstanceBranch;
            var instance = _instances.CreateInstanceMeta(
                branch,
                version,
                InstanceMeta.DefaultName,
                versionName: SelectedNewInstanceVersion.VersionName);
            _managedInstance = _instances.FindInstanceById(instance.Id);
            UpdateManagedInstancePresentation();
            IsInstanceCreatorOpen = false;
            ResetInstanceCreatorState();
            Volatile.Write(ref _logsInstanceId, null);
            InvalidateLogsRebuild();
            InstanceSection = string.Empty;
            RefreshManagedInstanceContent();
        }
        catch (Exception ex)
        {
            InstanceCreationError = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenInstanceDetails(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) ||
            string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
        {
            return;
        }

        var instance = _instances.GetCachedInstances()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, instanceId, StringComparison.Ordinal));
        if (instance is null)
            return;

        RefreshInstanceInstalledState(instance);
        _managedInstance = instance;
        UpdateManagedInstanceListSelection(instance.Id);
        Volatile.Write(ref _logsInstanceId, null);
        InvalidateLogsRebuild();
        InstanceSection = string.Empty;
        UpdateManagedInstancePresentation();
        RefreshManagedInstanceContent();
    }

    private void UpdateManagedInstanceListSelection(string managedInstanceId)
    {
        for (var index = 0; index < AllInstances.Count; index++)
        {
            var item = AllInstances[index];
            var isManaged = string.Equals(item.Id, managedInstanceId, StringComparison.Ordinal);
            if (item.IsManaged != isManaged)
                AllInstances[index] = item with { IsManaged = isManaged };
        }
    }

    [RelayCommand]
    private void SelectInstanceSection(string? section)
    {
        if (section is not ("mods" or "browse" or "worlds" or "logs"))
            return;

        var leavingLogs = section != "logs" && IsDisplayedInstanceLogsSection;
        if (section == "logs")
            Volatile.Write(ref _logsInstanceId, _managedInstance?.Id);
        else
        {
            Volatile.Write(ref _logsInstanceId, null);
            InvalidateLogsRebuild();
        }

        IsLogsLevelPopupOpen = false;

        DisplayedInstanceSectionTitle = GetInstanceSectionTitle(section);
        DisplayedInstanceSection = section;
        InstanceSection = section;
        InstanceContentError = string.Empty;
        if (leavingLogs)
            ApplyLogsLines([]);

        if (section == "mods" && !IsInstanceModsLoading &&
            !string.Equals(_modsLoadedForInstanceId, _managedInstance?.Id, StringComparison.Ordinal))
            _ = LoadInstalledModsAsync();
        else if (section == "browse")
        {
            _ = EnsureModCatalogFiltersAsync();
            if (ModCatalogItems.Count == 0)
                _ = SearchModCatalogAsync();
        }
        else if (section == "worlds" && !IsInstanceWorldsLoading &&
                 !string.Equals(_worldsLoadedForInstanceId, _managedInstance?.Id, StringComparison.Ordinal))
            _ = LoadInstanceWorldsAsync();
        else if (section == "logs")
            PrepareLogsForCurrentInstance();
    }

    [RelayCommand]
    private void CloseInstanceSection()
    {
        if (IsInstallingSelectedCatalogMods)
            return;

        Volatile.Write(ref _logsInstanceId, null);
        InvalidateLogsRebuild();
        InstanceSection = string.Empty;
        IsLogsLevelPopupOpen = false;
        InstanceContentError = string.Empty;
        IsModCatalogInstallConfirmationOpen = false;
        ResetModCatalogPreview();
    }

    internal void CompleteInstanceSectionClose()
    {
        if (IsInstanceOverviewSection)
        {
            var leavingLogs = IsDisplayedInstanceLogsSection;
            DisplayedInstanceSection = string.Empty;
            if (leavingLogs)
                ApplyLogsLines([]);
        }
    }

    internal void SynchronizeDisplayedInstanceSection()
    {
        DisplayedInstanceSection = InstanceSection;
    }

    [RelayCommand]
    private Task RefreshInstanceModsAsync()
        => LoadInstalledModsAsync();

    [RelayCommand]
    private Task SearchModCatalogAsync()
        => LoadModCatalogAsync(ModCatalogSearchQuery, false);

    [RelayCommand]
    private Task LoadMoreModCatalogAsync()
        => CanLoadMoreModCatalog
            ? LoadModCatalogAsync(ModCatalogSearchQuery, true)
            : Task.CompletedTask;

    [RelayCommand]
    private void ToggleModCatalogSelection(ModCatalogItemViewModel? item)
    {
        if (item is null || !item.CanSelect)
            return;

        item.IsSelected = !item.IsSelected;
        NotifyCatalogSelectionChanged();
    }

    [RelayCommand]
    private void ClearModCatalogSelection()
    {
        foreach (var item in ModCatalogItems.Where(item => item.IsSelected))
            item.IsSelected = false;

        NotifyCatalogSelectionChanged();
        if (IsModCatalogInstallConfirmationOpen)
            IsModCatalogInstallConfirmationOpen = false;
    }

    [RelayCommand]
    private async Task OpenModCatalogInstallConfirmation()
    {
        if (!CanOpenModCatalogInstallConfirmation)
            return;

        PrepareModCatalogInstallItems();
        _modCatalogInstallAccepted = false;
        ModCatalogInstallCompletedCount = 0;
        ModCatalogInstallProgress = 0;
        IsModCatalogInstallConfirmationOpen = true;

        await LoadModCatalogInstallDependenciesAsync(_modCatalogInstallItems.ToArray());
    }

    private async Task LoadModCatalogInstallDependenciesAsync(
        IReadOnlyList<ModCatalogInstallItemViewModel> items)
    {
        if (_modManager is null || items.Count == 0)
            return;

        var dependencyTasks = items.Select(async item =>
        {
            var task = _modManager.GetModDependenciesAsync(item.Id, item.CatalogItem.RecommendedFileId);
            return await (task ?? Task.FromResult<List<ModDependency>>([]));
        });
        var dependencies = await Task.WhenAll(dependencyTasks);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!_modCatalogInstallItems.Contains(item))
                continue;

            item.CatalogItem.SetDependencies(dependencies[index]);
        }

        FetchCatalogDependencyIcons(items);
    }

    [RelayCommand]
    private void CloseModCatalogInstallConfirmation()
    {
        if (IsInstallingSelectedCatalogMods)
            return;

        IsModCatalogInstallConfirmationOpen = false;
    }

    internal void CompleteModCatalogInstallConfirmationClose()
    {
        if (!IsModCatalogInstallConfirmationOpen &&
            !IsInstallingSelectedCatalogMods &&
            !_modCatalogInstallAccepted)
            DisposeModCatalogInstallItems();
    }

    internal void CompleteModCatalogInstallation()
    {
        DisposeModCatalogInstallItems();
        _modCatalogInstallAccepted = false;
    }

    [RelayCommand]
    private async Task InstallSelectedCatalogModsAsync()
    {
        if (!CanInstallSelectedCatalogMods || _modManager is null ||
            _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        if (_modCatalogInstallItems.Count == 0)
            PrepareModCatalogInstallItems();

        var installItems = _modCatalogInstallItems.ToArray();
        if (installItems.Length == 0)
            return;

        _modCatalogInstallAccepted = true;
        IsModCatalogInstallConfirmationOpen = false;
        IsInstallingSelectedCatalogMods = true;
        ModCatalogInstallCompletedCount = 0;
        ModCatalogInstallProgress = 0;
        InstanceContentError = string.Empty;
        var failed = false;
        try
        {
            for (var index = 0; index < installItems.Length; index++)
            {
                var installItem = installItems[index];
                var item = installItem.CatalogItem;
                if (!item.CanSelect)
                {
                    installItem.Fail();
                    failed = true;
                    ModCatalogInstallCompletedCount = index + 1;
                    ModCatalogInstallProgress = (index + 1) * 100d / installItems.Length;
                    continue;
                }

                item.IsInstalling = true;
                installItem.Begin();
                try
                {
                    if (!await _modManager.InstallModFileToInstanceAsync(
                            item.Id,
                            item.RecommendedFileId,
                            instancePath,
                            (stage, _) => Dispatcher.UIThread.Post(() =>
                            {
                                if (stage.Equals("downloading", StringComparison.OrdinalIgnoreCase))
                                    installItem.SetProgress(28);
                                else if (stage.Equals("installing", StringComparison.OrdinalIgnoreCase))
                                    installItem.SetProgress(72);
                                else if (stage.Equals("complete", StringComparison.OrdinalIgnoreCase))
                                    installItem.Complete();
                            })))
                    {
                        installItem.Fail();
                        failed = true;
                        continue;
                    }

                    item.IsInstalled = true;
                    item.InstalledFileId = item.RecommendedFileId;
                    item.IsSelected = false;
                    installItem.Complete();
                }
                catch
                {
                    installItem.Fail();
                    failed = true;
                }
                finally
                {
                    item.IsInstalling = false;
                    ModCatalogInstallCompletedCount = index + 1;
                    ModCatalogInstallProgress = (index + 1) * 100d / installItems.Length;
                }
            }

            await LoadInstalledModsAsync();
            if (failed)
                InstanceContentError = _localizer["modManager.installFailed"];
        }
        finally
        {
            IsInstallingSelectedCatalogMods = false;
            NotifyCatalogSelectionChanged();
        }
    }

    [RelayCommand]
    private async Task SelectModCatalogPreviewAsync(ModCatalogItemViewModel? item)
    {
        if (item is null || _modManager is null)
            return;

        if (ReferenceEquals(SelectedModCatalogPreview, item))
        {
            IsModCatalogPreviewOpen = true;
            return;
        }

        var previewVersion = ++_modCatalogPreviewVersion;
        SelectedModCatalogPreview = item;
        IsModCatalogPreviewOpen = true;
        SelectedModCatalogPreviewFile = null;
        _modCatalogPreviewFiles.Clear();
        OnPropertyChanged(nameof(HasModCatalogPreviewFiles));
        OnPropertyChanged(nameof(HasMultipleModCatalogPreviewScreenshots));
        ModCatalogPreviewScreenshotIndex = 0;
        IsModCatalogPreviewLoading = true;
        IsModCatalogPreviewFilesSkeletonVisible = true;
        IsModCatalogPreviewFilesSkeletonFadingOut = false;
        IsModCatalogPreviewFilesContentVisible = false;
        BeginModCatalogPreviewImagePreload(item, previewVersion);

        try
        {
            var result = await _modManager.GetModFilesAsync(item.Id, 0, 10);
            if (previewVersion != _modCatalogPreviewVersion ||
                !ReferenceEquals(SelectedModCatalogPreview, item))
            {
                return;
            }

            var files = result.Files.Select(file =>
            {
                var compatibility = ModCompatibilityEvaluator.Evaluate(
                    _modCatalogGameVersion,
                    file.GameVersions);
                return new ModCatalogFileItemViewModel(
                    file,
                    GetModReleaseLabel(file.ReleaseType),
                    string.Equals(file.Id, item.InstalledFileId, StringComparison.Ordinal),
                    compatibility,
                    GetModCompatibilityLabel(compatibility));
            });
            _modCatalogPreviewFiles.ReplaceRange(files);
            SelectedModCatalogPreviewFile = _modCatalogPreviewFiles.FirstOrDefault(file =>
                file.IsInstalled) ??
                _modCatalogPreviewFiles.FirstOrDefault(file =>
                    string.Equals(file.Id, item.RecommendedFileId, StringComparison.Ordinal)) ??
                _modCatalogPreviewFiles.FirstOrDefault(file => file.CanInstall) ??
                _modCatalogPreviewFiles.FirstOrDefault();
            if (SelectedModCatalogPreviewFile is not null)
                SelectedModCatalogPreviewFile.IsSelected = true;
            OnPropertyChanged(nameof(HasModCatalogPreviewFiles));
            OnPropertyChanged(nameof(CanInstallModCatalogPreview));
        }
        catch (Exception ex)
        {
            if (previewVersion == _modCatalogPreviewVersion)
                InstanceContentError = ex.Message;
        }
        finally
        {
            if (previewVersion == _modCatalogPreviewVersion)
            {
                IsModCatalogPreviewLoading = false;
                _ = RevealModCatalogPreviewFilesAsync(previewVersion);
            }
        }
    }

    [RelayCommand]
    private void CloseModCatalogPreview()
        => IsModCatalogPreviewOpen = false;

    internal void CompleteModCatalogPreviewClose()
    {
        if (!IsModCatalogPreviewOpen)
            ResetModCatalogPreview();
    }

    [RelayCommand]
    private void SelectModCatalogPreviewFile(ModCatalogFileItemViewModel? file)
    {
        if (file is not { CanSelect: true })
            return;

        foreach (var previewFile in ModCatalogPreviewFiles)
            previewFile.IsSelected = ReferenceEquals(previewFile, file);
        SelectedModCatalogPreviewFile = file;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowPreviousModCatalogScreenshotAsync()
        => MoveModCatalogPreviewScreenshotAsync(-1);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowNextModCatalogScreenshotAsync()
        => MoveModCatalogPreviewScreenshotAsync(1);

    [RelayCommand]
    private async Task InstallModCatalogPreviewAsync()
    {
        var item = SelectedModCatalogPreview;
        var file = SelectedModCatalogPreviewFile;
        if (item is null || file is null || item.IsInstalling || !file.CanInstall ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        item.IsInstalling = true;
        OnPropertyChanged(nameof(CanInstallModCatalogPreview));
        InstanceContentError = string.Empty;
        try
        {
            if (!await _modManager.InstallModFileToInstanceAsync(item.Id, file.Id, instancePath))
            {
                InstanceContentError = _localizer["instances.mods.installFailed"];
                return;
            }

            item.IsInstalled = true;
            item.InstalledFileId = file.Id;
            foreach (var previewFile in ModCatalogPreviewFiles)
                previewFile.IsInstalled = ReferenceEquals(previewFile, file);
            await LoadInstalledModsAsync();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            item.IsInstalling = false;
            OnPropertyChanged(nameof(CanInstallModCatalogPreview));
        }
    }

    [RelayCommand]
    private Task RefreshInstanceWorldsAsync()
        => LoadInstanceWorldsAsync();

    #region Installed mod management

    [RelayCommand]
    private async Task ToggleModAsync(InstanceModItemViewModel? item)
    {
        if (item is null || item.IsBusy ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        item.IsBusy = true;
        InstanceContentError = string.Empty;
        try
        {
            var target = !item.IsEnabled;
            var changed = await Task.Run(
                () => _modManager.SetModEnabledAsync(instancePath, item.Id, target));
            if (!changed)
            {
                InstanceContentError = _localizer["instances.mods.toggleFailed"];
                return;
            }

            item.IsEnabled = target;
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteModAsync(InstanceModItemViewModel? item)
    {
        if (item is null || item.IsBusy ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        item.IsBusy = true;
        InstanceContentError = string.Empty;
        try
        {
            var removed = await Task.Run(
                () => _modManager.RemoveInstalledModAsync(instancePath, item.Id));
            if (!removed)
                InstanceContentError = _localizer["instances.mods.deleteFailed"];
        }
        finally
        {
            item.IsBusy = false;
        }

        await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private void SelectAllInstalledMods()
    {
        foreach (var mod in VisibleInstalledMods)
            mod.IsSelected = true;
    }

    [RelayCommand]
    private void ClearInstalledModsSelection()
    {
        foreach (var mod in InstalledMods)
            mod.IsSelected = false;
    }

    [RelayCommand]
    private Task EnableSelectedModsAsync()
        => SetModsEnabledForSelectionAsync(true);

    [RelayCommand]
    private Task DisableSelectedModsAsync()
        => SetModsEnabledForSelectionAsync(false);

    private async Task SetModsEnabledForSelectionAsync(bool enabled)
    {
        var selected = InstalledMods.Where(mod => mod.IsSelected).ToList();
        if (selected.Count == 0 ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        InstanceContentError = string.Empty;
        foreach (var item in selected)
        {
            if (item.IsBusy)
                continue;

            item.IsBusy = true;
            try
            {
                var changed = await Task.Run(
                    () => _modManager.SetModEnabledAsync(instancePath, item.Id, enabled));
                if (changed)
                    item.IsEnabled = enabled;
                else
                    InstanceContentError = _localizer["instances.mods.toggleFailed"];
            }
            finally
            {
                item.IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedModsAsync()
    {
        var selected = InstalledMods.Where(mod => mod.IsSelected).ToList();
        if (selected.Count == 0 ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        InstanceContentError = string.Empty;
        foreach (var item in selected)
        {
            item.IsBusy = true;
            var removed = await Task.Run(
                () => _modManager.RemoveInstalledModAsync(instancePath, item.Id));
            if (!removed)
                InstanceContentError = _localizer["instances.mods.deleteFailed"];
        }

        await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private async Task CheckModUpdatesAsync()
    {
        if (_modManager is null || IsCheckingModUpdates ||
            _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsCheckingModUpdates = true;
        InstanceContentError = string.Empty;
        try
        {
            var updates = await Task.Run(
                () => _modManager.CheckInstanceModUpdatesAsync(instancePath));
            _modUpdatesById.Clear();
            foreach (var update in updates)
                _modUpdatesById[update.Id] = update;

            foreach (var item in InstalledMods)
            {
                item.UpdateVersion =
                    _modUpdatesById.TryGetValue(item.Id, out var update) &&
                    !string.IsNullOrWhiteSpace(update.LatestVersion)
                        ? update.LatestVersion
                        : _localizer["instances.mods.updateAvailableShort"];
            }

            ModUpdateCount = _modUpdatesById.Count;
        }
        finally
        {
            IsCheckingModUpdates = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllModsWithUpdatesAsync()
    {
        if (_modManager is null || IsApplyingModUpdates || !HasModUpdates ||
            _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsApplyingModUpdates = true;
        InstanceContentError = string.Empty;
        try
        {
            foreach (var (modId, update) in _modUpdatesById.ToList())
            {
                var installed = await _modManager.InstallModFileToInstanceAsync(
                    update.CurseForgeId,
                    update.LatestFileId,
                    instancePath);
                if (installed)
                    _modUpdatesById.Remove(modId);
                else
                    InstanceContentError = _localizer["instances.mods.installFailed"];
            }

            ModUpdateCount = _modUpdatesById.Count;
        }
        finally
        {
            IsApplyingModUpdates = false;
        }

        await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private async Task ImportModsAsync()
    {
        if (_filePicker is null)
            return;

        var files = await _filePicker.BrowseModFilesAsync();
        await ImportModFilesAsync(files);
    }

    public async Task ImportModFilesAsync(IReadOnlyList<string>? filePaths)
    {
        if (_modManager is null || _managedInstance?.IsInstalled != true)
            return;

        var paths = (filePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           (path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (paths.Count == 0)
            return;

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        InstanceContentError = string.Empty;
        var imported = false;
        foreach (var sourcePath in paths)
        {
            try
            {
                if (await Task.Run(() => _modManager.InstallLocalModFile(sourcePath, instancePath)))
                    imported = true;
            }
            catch (Exception ex)
            {
                InstanceContentError = ex.Message;
            }
        }

        if (imported)
            await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private Task OpenInstalledModPageAsync(InstanceModItemViewModel? item)
        => item is null
            ? Task.CompletedTask
            : OpenExternalAsync(item.CurseForgeUrl);

    [RelayCommand]
    private Task OpenCatalogModPageAsync(ModCatalogItemViewModel? item)
        => item is null
            ? Task.CompletedTask
            : OpenExternalAsync(item.CurseForgeUrl);

    private async Task OpenExternalAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            await _uriLauncher.LaunchAsync(uri);
        }
    }

    private void UnsubscribeInstalledModItems()
    {
        foreach (var item in InstalledMods)
            item.PropertyChanged -= OnInstalledModItemPropertyChanged;
    }

    private void OnInstalledModItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is InstanceModItemViewModel &&
            args.PropertyName == nameof(InstanceModItemViewModel.IsSelected))
        {
            RecalculateInstalledModsSelection();
        }
    }


    private void RecalculateInstalledModsSelection()
    {
        SelectedModCount = InstalledMods.Count(mod => mod.IsSelected);
    }

    private void RestartModIconFetch()
    {
        _modIconsCancellation.Cancel();
        _modIconsCancellation.Dispose();
        _modIconsCancellation = new CancellationTokenSource();
    }

    private void FetchInstalledModIcons(IEnumerable<InstanceModItemViewModel> items)
    {
        if (_remoteImageCache is null)
            return;

        var token = _modIconsCancellation.Token;
        var targets = items
            .Where(item => !string.IsNullOrWhiteSpace(item.IconUrl) && item.Icon is null)
            .ToList();
        if (targets.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            foreach (var item in targets)
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    var bitmap = await RemoteBitmapLoader.LoadAsync(
                        item.IconUrl,
                        96,
                        _httpClient,
                        token,
                        _remoteImageCache);
                    if (bitmap is not null && !token.IsCancellationRequested)
                        Dispatcher.UIThread.Post(() => item.Icon = bitmap);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                }
            }
        }, token);
    }

    private void FetchCatalogModIcons(IEnumerable<ModCatalogItemViewModel> items)
    {
        if (_remoteImageCache is null)
            return;

        var token = _modIconsCancellation.Token;
        var targets = items
            .Where(item =>
                (!string.IsNullOrWhiteSpace(item.IconUrl) && item.Icon is null) ||
                (!string.IsNullOrWhiteSpace(item.AuthorAvatarUrl) && item.AuthorAvatar is null))
            .ToList();
        if (targets.Count == 0)
            return;

        _ = Parallel.ForEachAsync(
            targets,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 4
            },
            async (item, cancellationToken) =>
            {
                try
                {
                    var iconTask = item.Icon is null
                        ? RemoteBitmapLoader.LoadAsync(
                            item.IconUrl,
                            112,
                            _httpClient,
                            cancellationToken,
                            _remoteImageCache,
                            "mods")
                        : Task.FromResult<Bitmap?>(null);
                    var avatarTask = LoadCatalogAuthorAvatarAsync(item, cancellationToken);
                    await Task.WhenAll(iconTask, avatarTask).ConfigureAwait(false);

                    var icon = await iconTask.ConfigureAwait(false);
                    var authorAvatar = await avatarTask.ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        icon?.Dispose();
                        authorAvatar?.Dispose();
                        return;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (cancellationToken.IsCancellationRequested ||
                            !ModCatalogItems.Contains(item))
                        {
                            icon?.Dispose();
                            authorAvatar?.Dispose();
                            return;
                        }

                        if (icon is not null)
                            item.Icon = icon;
                        if (authorAvatar is not null)
                            item.AuthorAvatar = authorAvatar;
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch
                {
                }
            });
    }

    private void FetchCatalogDependencyIcons(
        IEnumerable<ModCatalogInstallItemViewModel> items)
    {
        if (_remoteImageCache is null)
            return;

        var token = _modDependencyIconsCancellation.Token;
        var targets = items
            .SelectMany(item => item.DependencyItems)
            .Where(item => item.Icon is null && !string.IsNullOrWhiteSpace(item.IconUrl))
            .ToList();
        if (targets.Count == 0)
            return;

        _ = Parallel.ForEachAsync(
            targets,
            new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = 4
            },
            async (item, cancellationToken) =>
            {
                try
                {
                    var icon = await RemoteBitmapLoader.LoadAsync(
                            item.IconUrl,
                            64,
                            _httpClient,
                            cancellationToken,
                            _remoteImageCache,
                            "mod-dependencies")
                        .ConfigureAwait(false);
                    if (icon is null || cancellationToken.IsCancellationRequested)
                    {
                        icon?.Dispose();
                        return;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            icon.Dispose();
                            return;
                        }

                        item.Icon = icon;
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Debug("InstancesViewModel", $"Could not load dependency icon: {ex.Message}");
                }
            });
    }

    private async Task<Bitmap?> LoadCatalogAuthorAvatarAsync(
        ModCatalogItemViewModel item,
        CancellationToken cancellationToken)
    {
        if (item.AuthorAvatar is not null)
            return null;

        return await RemoteBitmapLoader.LoadAsync(
                item.AuthorAvatarUrl,
                48,
                _httpClient,
                cancellationToken,
                _remoteImageCache,
                "mod-authors")
            .ConfigureAwait(false);
    }

    private async Task MoveModCatalogPreviewScreenshotAsync(int offset)
    {
        if (IsModCatalogPreviewImageTransitioning ||
            SelectedModCatalogPreview is not { ScreenshotUrls.Count: > 1 } item)
        {
            return;
        }

        var targetIndex = Math.Clamp(
            ModCatalogPreviewScreenshotIndex + offset,
            0,
            item.ScreenshotUrls.Count - 1);
        if (targetIndex == ModCatalogPreviewScreenshotIndex)
            return;

        _modPreviewImageTransitionCancellation.Cancel();
        _modPreviewImageTransitionCancellation.Dispose();
        _modPreviewImageTransitionCancellation = new CancellationTokenSource();
        var cancellationToken = _modPreviewImageTransitionCancellation.Token;
        IsModCatalogPreviewImageTransitioning = true;
        _isModCatalogPreviewImageFadingOut = true;
        IsModCatalogPreviewImageVisible = false;

        try
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isModCatalogPreviewImageFadingOut = false;
                ModCatalogPreviewScreenshotIndex = targetIndex;
                ApplyModCatalogPreviewScreenshot(_modCatalogPreviewVersion);
            }, DispatcherPriority.Render, cancellationToken);
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => IsModCatalogPreviewImageTransitioning = false,
                    DispatcherPriority.Render);
            }
        }
    }

    private async Task RevealModCatalogPreviewFilesAsync(int previewVersion)
    {
        _modPreviewRevealCancellation.Cancel();
        _modPreviewRevealCancellation.Dispose();
        _modPreviewRevealCancellation = new CancellationTokenSource();
        var token = _modPreviewRevealCancellation.Token;
        try
        {
            await Task.Delay(ModCatalogPreviewFilesSkeletonMinMilliseconds, token)
                .ConfigureAwait(false);
            if (previewVersion != _modCatalogPreviewVersion)
                return;

            await Dispatcher.UIThread.InvokeAsync(
                () => IsModCatalogPreviewFilesSkeletonFadingOut = true,
                DispatcherPriority.Render);
            await Task.Delay(ModCatalogPreviewFilesFadeMilliseconds, token)
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (previewVersion != _modCatalogPreviewVersion)
                    return;

                IsModCatalogPreviewFilesSkeletonVisible = false;
                IsModCatalogPreviewFilesSkeletonFadingOut = false;
            }, DispatcherPriority.Render);

            await Task.Delay(16, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(
                () => IsModCatalogPreviewFilesContentVisible = true,
                DispatcherPriority.Render);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void BeginModCatalogPreviewImagePreload(
        ModCatalogItemViewModel item,
        int previewVersion)
    {
        _modPreviewImageCancellation.Cancel();
        _modPreviewImageCancellation.Dispose();
        _modPreviewImageCancellation = new CancellationTokenSource();
        var token = _modPreviewImageCancellation.Token;

        DisposeModCatalogPreviewBitmaps();

        IReadOnlyList<string> urls = item.ScreenshotUrls.Count > 0
            ? item.ScreenshotUrls
            : string.IsNullOrWhiteSpace(item.IconUrl)
                ? []
                : [item.IconUrl];

        if (urls.Count == 0)
        {
            ModCatalogPreviewImage = null;
            IsModCatalogPreviewImageVisible = false;
            IsModCatalogPreviewImageLoading = false;
            return;
        }

        lock (_modCatalogPreviewBitmapsLock)
        {
            for (var index = 0; index < urls.Count; index++)
            {
                _modCatalogPreviewBitmaps.Add(null);
                _modCatalogPreviewBitmapSlotsResolved.Add(false);
            }
        }

        IsModCatalogPreviewImageLoading = true;
        _ = Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(3, 3);
            var loads = urls.Select(async (url, index) =>
            {
                await gate.WaitAsync(token);
                try
                {
                    var bitmap = await RemoteBitmapLoader.LoadAsync(
                        url,
                        720,
                        _httpClient,
                        token,
                        _remoteImageCache);
                    var stored = false;
                    lock (_modCatalogPreviewBitmapsLock)
                    {
                        if (!token.IsCancellationRequested &&
                            previewVersion == _modCatalogPreviewVersion &&
                            index < _modCatalogPreviewBitmaps.Count &&
                            !_modCatalogPreviewBitmapSlotsResolved[index])
                        {
                            _modCatalogPreviewBitmaps[index] = bitmap;
                            _modCatalogPreviewBitmapSlotsResolved[index] = true;
                            stored = true;
                        }
                    }

                    if (!stored)
                    {
                        bitmap?.Dispose();
                        return;
                    }

                    Dispatcher.UIThread.Post(
                        () => ApplyModCatalogPreviewScreenshot(previewVersion),
                        DispatcherPriority.Background);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    gate.Release();
                }
            });

            try
            {
                await Task.WhenAll(loads);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void ApplyModCatalogPreviewScreenshot(int previewVersion)
    {
        if (previewVersion != _modCatalogPreviewVersion ||
            SelectedModCatalogPreview is null)
        {
            return;
        }

        Bitmap? bitmap = null;
        var resolved = false;
        lock (_modCatalogPreviewBitmapsLock)
        {
            if (ModCatalogPreviewScreenshotIndex < _modCatalogPreviewBitmaps.Count)
            {
                bitmap = _modCatalogPreviewBitmaps[ModCatalogPreviewScreenshotIndex];
                resolved = _modCatalogPreviewBitmapSlotsResolved[ModCatalogPreviewScreenshotIndex];
            }
        }

        IsModCatalogPreviewImageLoading = !resolved;
        ModCatalogPreviewImage = resolved
            ? bitmap ?? GetModCatalogPreviewFallbackBitmap()
            : null;
        IsModCatalogPreviewImageVisible = !_isModCatalogPreviewImageFadingOut &&
                                          ModCatalogPreviewImage is not null;
    }

    private Bitmap? GetModCatalogPreviewFallbackBitmap()
    {
        lock (_modCatalogPreviewBitmapsLock)
        {
            for (var offset = 1; offset < _modCatalogPreviewBitmaps.Count; offset++)
            {
                var candidate =
                    _modCatalogPreviewBitmaps[
                        (ModCatalogPreviewScreenshotIndex + offset) % _modCatalogPreviewBitmaps.Count];
                if (candidate is not null)
                    return candidate;
            }
        }

        return SelectedModCatalogPreview?.Icon;
    }

    private void DisposeModCatalogPreviewBitmaps()
    {
        lock (_modCatalogPreviewBitmapsLock)
        {
            foreach (var bitmap in _modCatalogPreviewBitmaps)
                bitmap?.Dispose();
            _modCatalogPreviewBitmaps.Clear();
            _modCatalogPreviewBitmapSlotsResolved.Clear();
        }

        ModCatalogPreviewImage = null;
        IsModCatalogPreviewImageVisible = false;
    }

    private void ResetModCatalogPreview()
    {
        _modCatalogPreviewVersion++;
        _modPreviewImageCancellation.Cancel();
        _modPreviewImageTransitionCancellation.Cancel();
        _modPreviewRevealCancellation.Cancel();
        IsModCatalogPreviewOpen = false;
        SelectedModCatalogPreview = null;
        SelectedModCatalogPreviewFile = null;
        _modCatalogPreviewFiles.Clear();
        IsModCatalogPreviewLoading = false;
        IsModCatalogPreviewImageLoading = false;
        _isModCatalogPreviewImageFadingOut = false;
        IsModCatalogPreviewImageVisible = false;
        IsModCatalogPreviewImageTransitioning = false;
        IsModCatalogPreviewFilesSkeletonVisible = false;
        IsModCatalogPreviewFilesSkeletonFadingOut = false;
        IsModCatalogPreviewFilesContentVisible = false;
        ModCatalogPreviewScreenshotIndex = 0;
        DisposeModCatalogPreviewBitmaps();
        OnPropertyChanged(nameof(HasModCatalogPreviewFiles));
        OnPropertyChanged(nameof(HasMultipleModCatalogPreviewScreenshots));
        OnPropertyChanged(nameof(CanInstallModCatalogPreview));
    }

    private string GetModReleaseLabel(int releaseType) => releaseType switch
    {
        2 => _localizer["modManager.releaseType.beta"],
        3 => _localizer["modManager.releaseType.alpha"],
        _ => _localizer["modManager.releaseType.release"]
    };

    private string GetModCompatibilityLabel(ModCompatibilityStatus compatibility) => compatibility switch
    {
        ModCompatibilityStatus.Compatible => _localizer["instances.mods.compatibility.compatible"],
        ModCompatibilityStatus.Incompatible => _localizer["instances.mods.compatibility.incompatible"],
        _ => _localizer["instances.mods.compatibility.unknown"]
    };

    private void NotifyCatalogSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCatalogModCount));
        OnPropertyChanged(nameof(HasSelectedCatalogMods));
        OnPropertyChanged(nameof(CanInstallSelectedCatalogMods));
        OnPropertyChanged(nameof(CanOpenModCatalogInstallConfirmation));
        OnPropertyChanged(nameof(ModCatalogInstallSelectedLabel));
    }

    #endregion

    #region Mod catalog filters

    private async Task EnsureModCatalogFiltersAsync()
    {
        if (_modManager is null || _modCatalogFiltersLoaded ||
            ModCatalogCategories.Count > 0)
        {
            return;
        }

        _modCatalogFiltersLoaded = true;
        try
        {
            var categories = await _modManager.GetModCategoriesAsync();
            if (categories is not { Count: > 0 })
            {
                _modCatalogFiltersLoaded = false;
                return;
            }

            _loadedModCategories = categories;
            RebuildModCatalogCategories();
        }
        catch
        {
            _modCatalogFiltersLoaded = false;
        }
    }

    private void RebuildModCatalogCategories()
    {
        var selectedValue = SelectedModCatalogCategory?.Value;
        _suppressCatalogReload = true;
        ModCatalogCategories.Clear();
        ModCatalogCategories.Add(
            new InstanceListOptionViewModel("all", _localizer["instances.browse.categoryAll"]));
        foreach (var category in _loadedModCategories)
        {
            if (category.Id == 0 ||
                string.Equals(category.Slug, "all", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var categoryValue = category.Id > 0
                ? category.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"fallback:{category.Slug}";
            ModCatalogCategories.Add(new InstanceListOptionViewModel(
                categoryValue,
                LocalizeModCatalogCategory(category)));
        }

        SelectedModCatalogCategory =
            ModCatalogCategories.FirstOrDefault(option =>
                string.Equals(option.Value, selectedValue, StringComparison.Ordinal)) ??
            ModCatalogCategories[0];
        _suppressCatalogReload = false;
    }

    private string LocalizeModCatalogCategory(ModCategory category)
    {
        var slug = category.Slug.Trim().Replace('_', '-').ToLowerInvariant();
        return ModCatalogCategoryResourceKeys.TryGetValue(slug, out var resourceKey)
            ? _localizer[resourceKey]
            : category.Name;
    }

    private void BuildModCatalogSortOptions()
    {
        var selectedValue = SelectedModCatalogSort?.Value ?? "2";
        _suppressCatalogReload = true;
        ModCatalogSortOptions.Clear();
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("1", _localizer["instances.browse.sortRelevancy"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("2", _localizer["instances.browse.sortPopularity"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("3", _localizer["instances.browse.sortLatestUpdate"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("11", _localizer["instances.browse.sortCreationDate"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("6", _localizer["instances.browse.sortTotalDownloads"]));
        SelectedModCatalogSort =
            ModCatalogSortOptions.FirstOrDefault(option => option.Value == selectedValue) ??
            ModCatalogSortOptions[1];
        _suppressCatalogReload = false;
    }

    partial void OnSelectedModCatalogCategoryChanged(InstanceListOptionViewModel? value)
    {
        if (!_suppressCatalogReload)
            _ = SearchModCatalogAsync();
    }

    partial void OnSelectedModCatalogSortChanged(InstanceListOptionViewModel? value)
    {
        if (!_suppressCatalogReload)
            _ = SearchModCatalogAsync();
    }

    #endregion

    #region Game console

    partial void OnIsLogsDebugEnabledChanged(bool value) => OnLogsLevelChanged();
    partial void OnIsLogsWarningsEnabledChanged(bool value) => OnLogsLevelChanged();
    partial void OnIsLogsErrorsEnabledChanged(bool value) => OnLogsLevelChanged();
    partial void OnIsLogsTracingEnabledChanged(bool value) => OnLogsLevelChanged();

    private void OnLogsLevelChanged()
    {
        OnPropertyChanged(nameof(SelectedLogsLevelCount));
        OnPropertyChanged(nameof(LogsLevelSummary));
        OnPropertyChanged(nameof(HasAdditionalLogsLevels));
        OnPropertyChanged(nameof(LogsAdditionalLevelCountText));
        RebuildLogsLines();
    }

    partial void OnLogsSearchQueryChanged(string value)
        => RebuildLogsLines();

    private void OnConsoleLineReceived(object? sender, GameConsoleLineEventArgs e)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
            return;

        var instanceId = Volatile.Read(ref _logsInstanceId);
        if (string.IsNullOrWhiteSpace(instanceId) ||
            !string.Equals(e.Line.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return;
        }

        lock (_pendingConsoleLock)
        {
            if (_logsDisplayedLines.Remove(e.Line))
                return;

            _pendingConsoleLines.Add(e.Line);
            _consoleLinesVersion++;
            if (_pendingConsoleLines.Count > MaxConsoleLines * 2)
            {
                _pendingConsoleLines.RemoveRange(0, _pendingConsoleLines.Count - MaxConsoleLines);
            }
        }

        if (Volatile.Read(ref _logsRebuildInProgress) == 0 &&
            Interlocked.Exchange(ref _consoleFlushScheduled, 1) == 0)
            Dispatcher.UIThread.Post(FlushPendingConsoleLines);
    }

    private void FlushPendingConsoleLines()
    {
        Interlocked.Exchange(ref _consoleFlushScheduled, 0);
        if (Volatile.Read(ref _isDisposed) == 0 &&
            Volatile.Read(ref _logsRebuildInProgress) == 0 &&
            IsInstanceLogsSection)
        {
            FlushConsoleLines();
        }
    }

    private void FlushConsoleLines()
    {
        List<GameConsoleLine> pending;
        lock (_pendingConsoleLock)
        {
            if (_pendingConsoleLines.Count == 0)
                return;

            pending = _pendingConsoleLines
                .Where(line => !_logsDisplayedLines.Remove(line))
                .ToList();
            _pendingConsoleLines.Clear();
        }

        var instanceId = _managedInstance?.Id;
        if (string.IsNullOrWhiteSpace(instanceId))
            return;

        var filter = LogsSearchQuery.Trim();
        var filteredLines = pending
            .Where(line => string.Equals(line.InstanceId, instanceId, StringComparison.Ordinal) &&
                MatchesConsoleFilter(line, filter) && MatchesLogsLevel(
                line,
                IsLogsDebugEnabled,
                IsLogsWarningsEnabled,
                IsLogsErrorsEnabled,
                IsLogsTracingEnabled))
            .Select(ToLogLineViewModel)
            .TakeLast(MaxConsoleLines)
            .ToList();

        if (filteredLines.Count == 0)
            return;

        var excessCount = _consoleLines.Count + filteredLines.Count - MaxConsoleLines;
        if (excessCount > 0)
            _consoleLines.RemoveRange(0, excessCount);
        _consoleLines.AddRange(filteredLines);

        LogsRevision++;
        NotifyLogsStateChanged();
    }

    private void RebuildLogsLines()
    {
        var rebuildVersion = Interlocked.Increment(ref _logsRebuildVersion);
        _logsRebuildCancellation?.Cancel();
        _logsRebuildCancellation?.Dispose();
        _logsRebuildCancellation = null;
        Interlocked.Exchange(ref _logsRebuildInProgress, 0);

        var instanceId = _managedInstance?.Id;
        if (string.IsNullOrWhiteSpace(instanceId) || _gameConsole is null)
        {
            lock (_pendingConsoleLock)
                _pendingConsoleLines.Clear();

            ApplyLogsLines([]);
            return;
        }

        var filter = LogsSearchQuery.Trim();
        var showDebug = IsLogsDebugEnabled;
        var showWarnings = IsLogsWarningsEnabled;
        var showErrors = IsLogsErrorsEnabled;
        var showTracing = IsLogsTracingEnabled;
        int consoleLinesVersion;
        lock (_pendingConsoleLock)
        {
            _pendingConsoleLines.Clear();
            consoleLinesVersion = _consoleLinesVersion;
        }

        var snapshot = _gameConsole.GetLines(instanceId);
        if (snapshot.Count <= SynchronousLogsRefreshLimit)
        {
            var filteredLines = FilterLogsLines(
                snapshot,
                filter,
                showDebug,
                showWarnings,
                showErrors,
                showTracing,
                CancellationToken.None);
            if (!IsLogsSnapshotCurrent(rebuildVersion, consoleLinesVersion, instanceId))
            {
                if (rebuildVersion == Volatile.Read(ref _logsRebuildVersion) && IsInstanceLogsSection)
                    RebuildLogsLines();
                return;
            }

            ApplyLogsLines(filteredLines);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _logsRebuildCancellation = cancellation;
        Interlocked.Exchange(ref _logsRebuildInProgress, 1);
        _ = RebuildLogsLinesAsync(
            snapshot,
            filter,
            showDebug,
            showWarnings,
            showErrors,
            showTracing,
            instanceId,
            rebuildVersion,
            cancellation);
    }

    private async Task RebuildLogsLinesAsync(
        IReadOnlyList<GameConsoleLine> snapshot,
        string filter,
        bool showDebug,
        bool showWarnings,
        bool showErrors,
        bool showTracing,
        string instanceId,
        long rebuildVersion,
        CancellationTokenSource cancellation)
    {
        List<InstanceLogLineViewModel> filteredLines;
        var cancellationToken = cancellation.Token;
        try
        {
            filteredLines = await Task.Run(
                () => FilterLogsLines(
                    snapshot,
                    filter,
                    showDebug,
                    showWarnings,
                    showErrors,
                    showTracing,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await ApplyLogsLinesBatchedAsync(
                filteredLines,
                instanceId,
                rebuildVersion,
                cancellation,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ApplyLogsLinesBatchedAsync(
        IReadOnlyList<InstanceLogLineViewModel> lines,
        string instanceId,
        long rebuildVersion,
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken)
    {
        var started = await Dispatcher.UIThread.InvokeAsync(
            () => BeginBatchedLogsApply(lines, instanceId, rebuildVersion),
            DispatcherPriority.Background,
            cancellationToken);
        if (!started)
            return;

        for (var offset = 0; offset < lines.Count; offset += LogsRenderBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = lines.Skip(offset).Take(LogsRenderBatchSize).ToArray();
            var appended = await Dispatcher.UIThread.InvokeAsync(
                () => AppendBatchedLogs(batch, instanceId, rebuildVersion),
                DispatcherPriority.Background,
                cancellationToken);
            if (!appended)
                return;

            if (offset + batch.Length < lines.Count)
                await Task.Delay(LogsRenderBatchDelay, cancellationToken).ConfigureAwait(false);
        }

        await Dispatcher.UIThread.InvokeAsync(
            () => CompleteBatchedLogsApply(instanceId, rebuildVersion, cancellation),
            DispatcherPriority.Background,
            cancellationToken);
    }

    private bool BeginBatchedLogsApply(
        IReadOnlyList<InstanceLogLineViewModel> lines,
        string instanceId,
        long rebuildVersion)
    {
        if (!IsLogsRebuildCurrent(rebuildVersion, instanceId))
            return false;

        lock (_pendingConsoleLock)
        {
            _logsDisplayedLines.Clear();
            foreach (var line in lines)
                _logsDisplayedLines.Add(line.OriginalLine);
        }

        if (_consoleLines.Count > 0)
            _consoleLines.ReplaceRange([]);
        return true;
    }

    private bool AppendBatchedLogs(
        IReadOnlyList<InstanceLogLineViewModel> batch,
        string instanceId,
        long rebuildVersion)
    {
        if (!IsLogsRebuildCurrent(rebuildVersion, instanceId))
            return false;

        _consoleLines.AddRange(batch);
        if (_consoleLines.Count == batch.Count)
            NotifyLogsStateChanged();

        return true;
    }

    private void CompleteBatchedLogsApply(
        string instanceId,
        long rebuildVersion,
        CancellationTokenSource cancellation)
    {
        if (!IsLogsRebuildCurrent(rebuildVersion, instanceId))
            return;

        if (ReferenceEquals(_logsRebuildCancellation, cancellation))
        {
            _logsRebuildCancellation = null;
            cancellation.Dispose();
        }

        Interlocked.Exchange(ref _logsRebuildInProgress, 0);
        LogsRevision++;
        NotifyLogsStateChanged();
        SchedulePendingConsoleFlush();
    }

    private bool IsLogsRebuildCurrent(long rebuildVersion, string instanceId)
        => Volatile.Read(ref _isDisposed) == 0 &&
           rebuildVersion == Volatile.Read(ref _logsRebuildVersion) &&
           IsInstanceLogsSection &&
           string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal);

    private static List<InstanceLogLineViewModel> FilterLogsLines(
        IReadOnlyList<GameConsoleLine> lines,
        string filter,
        bool showDebug,
        bool showWarnings,
        bool showErrors,
        bool showTracing,
        CancellationToken cancellationToken)
    {
        var filtered = new List<InstanceLogLineViewModel>(Math.Min(lines.Count, MaxConsoleLines));
        for (var index = 0; index < lines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[index];
            if (!MatchesConsoleFilter(line, filter) ||
                !MatchesLogsLevel(line, showDebug, showWarnings, showErrors, showTracing))
            {
                continue;
            }

            filtered.Add(ToLogLineViewModel(line));
        }

        if (filtered.Count > MaxConsoleLines)
            filtered.RemoveRange(0, filtered.Count - MaxConsoleLines);

        return filtered;
    }

    private bool IsLogsSnapshotCurrent(long rebuildVersion, int consoleLinesVersion, string instanceId)
    {
        if (rebuildVersion != Volatile.Read(ref _logsRebuildVersion) ||
            !IsInstanceLogsSection ||
            !string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
        {
            return false;
        }

        lock (_pendingConsoleLock)
            return _consoleLinesVersion == consoleLinesVersion;
    }

    private void ApplyLogsLines(IReadOnlyList<InstanceLogLineViewModel> lines)
    {
        lock (_pendingConsoleLock)
        {
            if (lines.Count == 0 && !IsInstanceLogsSection)
                _pendingConsoleLines.Clear();

            _logsDisplayedLines.Clear();
            foreach (var line in lines)
                _logsDisplayedLines.Add(line.OriginalLine);
            Interlocked.Exchange(ref _logsRebuildInProgress, 0);
        }

        _consoleLines.ReplaceRange(lines);
        LogsRevision++;
        NotifyLogsStateChanged();
        SchedulePendingConsoleFlush();
    }

    private void InvalidateLogsRebuild()
    {
        Interlocked.Increment(ref _logsRebuildVersion);
        _logsRebuildCancellation?.Cancel();
        _logsRebuildCancellation?.Dispose();
        _logsRebuildCancellation = null;
        Interlocked.Exchange(ref _logsRebuildInProgress, 0);
    }

    private void SchedulePendingConsoleFlush()
    {
        if (Volatile.Read(ref _isDisposed) != 0 ||
            Volatile.Read(ref _logsRebuildInProgress) != 0 ||
            !IsInstanceLogsSection)
        {
            return;
        }

        lock (_pendingConsoleLock)
        {
            if (_pendingConsoleLines.Count == 0)
                return;
        }

        if (Interlocked.Exchange(ref _consoleFlushScheduled, 1) == 0)
            Dispatcher.UIThread.Post(FlushPendingConsoleLines);
    }

    private void PrepareLogsForCurrentInstance()
    {
        RebuildLogsLines();
    }

    [RelayCommand]
    private void ClearLogs()
    {
        if (_managedInstance is { } instance)
            _gameConsole?.Clear(instance.Id);

        RebuildLogsLines();
    }

    private static bool MatchesConsoleFilter(GameConsoleLine line, string filter)
        => string.IsNullOrEmpty(filter) ||
           line.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
           line.Source.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
           line.Level.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private static bool MatchesLogsLevel(
        GameConsoleLine line,
        bool showDebug,
        bool showWarnings,
        bool showErrors,
        bool showTracing)
        => line.IsTrace || line.Level == "TRACE" ? showTracing :
           line.Level == "WARN" ? showWarnings :
           line.Level == "ERROR" ? showErrors :
           showDebug;

    private static InstanceLogLineViewModel ToLogLineViewModel(GameConsoleLine line)
        => new(
            line.Level,
            line.Timestamp.ToLocalTime().ToString("HH:mm:ss.ffff"),
            line.Source,
            line.Text,
            line.IsTrace)
        {
            OriginalLine = line
        };

    private void NotifyLogsStateChanged()
    {
        OnPropertyChanged(nameof(HasLogs));
        OnPropertyChanged(nameof(IsLogsEmpty));
        OnPropertyChanged(nameof(CanShowLogsInFolder));
    }

    [RelayCommand]
    private async Task ShowLogsInFolderAsync()
    {
        if (!CanShowLogsInFolder || _logSession is null)
            return;

        await _uriLauncher.LaunchDirectoryAsync(_logSession.SessionDirectory);
    }

    #endregion

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunManagedInstanceAsync()
    {
        if (_managedInstance is not { } instance || !CanRunManagedInstanceAction)
            return;

        if (IsManagedInstanceActionActive)
        {
            if (!IsManagedInstanceCancellationArmed)
                return;

            if (IsManagedInstanceRunning)
                _gameProcess.ExitGame(instance.Id);
            else
                CancelActivity();

            return;
        }

        _managedInstanceActionInstanceId = instance.Id;
        _managedInstanceActionStartedAtUtc = DateTime.UtcNow;
        _managedInstanceGameStartedAtUtc = null;
        _managedInstanceActionStartedWithInstall = !instance.IsInstalled;
        var actionGeneration = ++_managedInstanceActionGeneration;
        _isManagedInstanceCancellationArmed = false;
        BeginInstanceActivity(instance.Id);
        CanCancelActivity = true;
        IsActivityVisible = true;
        ActivityProgress = 0;
        ActivityProgressText = "0%";
        ActivityTitle = _localizer["common.loading"];
        ActivityDetail = FormatInstanceName(instance.Name, instance.Version, instance.VersionName);
        NotifyManagedInstanceActionStateChanged();
        _managedInstanceActionTimer.Start();

        try
        {
            if (instance.IsInstalled)
            {
                await Task.Run(() => _gameLaunchCoordinator.LaunchAsync(
                    instance.Id,
                    authorizationUriPresenter: _uriLauncher.LaunchAsync));
            }
            else
            {
                var result = await Task.Run(() => _installationWorkflow.DownloadAndLaunchInstanceAsync(
                    instance.Id,
                    _uriLauncher.LaunchAsync));
                if (!result.Success && !result.Cancelled && !string.IsNullOrWhiteSpace(result.Error))
                    ShowError(result.Error);
            }
        }
        finally
        {
            if (actionGeneration == _managedInstanceActionGeneration)
            {
                CanCancelActivity = false;
                RefreshInstances();
                if (!IsManagedInstanceRunning)
                    EndManagedInstanceAction();
                else
                    NotifyManagedInstanceActionStateChanged();
            }

            if (!_completedManagedActivityGenerations.Remove(actionGeneration))
                EndInstanceActivity(instance.Id);
        }
    }

    [RelayCommand]
    private async Task OpenManagedInstanceFolderAsync()
    {
        if (_managedInstance is null)
            return;

        var path = _instances.GetInstancePathById(_managedInstance.Id);
        if (!string.IsNullOrWhiteSpace(path))
            await _uriLauncher.LaunchDirectoryAsync(path);
    }

    [RelayCommand]
    private void BeginEditManagedInstance()
    {
        if (_managedInstance is not { } instance || !CanEditManagedInstance)
            return;

        var path = _instances.GetInstancePathById(instance.Id);
        var meta = path is null ? null : _instances.GetInstanceMeta(path);
        if (meta is null)
        {
            ShowError(_localizer["instances.editor.saveFailed"]);
            return;
        }

        _editingInstanceId = instance.Id;
        _instanceIconPickerGeneration++;
        IsChoosingInstanceIcon = false;
        EditInstanceName = meta.Name;
        EditInstanceNotes = meta.Notes ?? string.Empty;
        _replaceInstanceIcon = false;
        _removeInstanceIcon = false;
        InstanceEditorError = string.Empty;
        ReplaceEditInstanceIcon(LoadInstanceIcon(path!));
        IsInstanceEditMounted = true;
        IsEditingInstance = true;
    }

    [RelayCommand]
    private void CancelEditManagedInstance()
    {
        IsEditingInstance = false;
        _instanceIconPickerGeneration++;
        IsChoosingInstanceIcon = false;
        _editingInstanceId = null;
        _replaceInstanceIcon = false;
        _removeInstanceIcon = false;
        InstanceEditorError = string.Empty;
    }

    public void CompleteInstanceEditClose()
    {
        ReplaceEditInstanceIcon(null);
        IsInstanceEditMounted = false;
    }

    [RelayCommand]
    private async Task ChooseInstanceIconAsync()
    {
        if (!IsEditingInstance || _filePicker is null || IsChoosingInstanceIcon)
            return;

        var editingId = _editingInstanceId;
        var generation = ++_instanceIconPickerGeneration;
        IsChoosingInstanceIcon = true;

        try
        {
            var chosenPath = await _filePicker.BrowseImageAsync(EditInstanceIconLabel);
            if (string.IsNullOrWhiteSpace(chosenPath) || !IsEditingInstance ||
                _editingInstanceId != editingId || generation != _instanceIconPickerGeneration)
                return;

            using var stream = File.OpenRead(chosenPath);
            var icon = DecodeInstanceIcon(stream);
            ReplaceEditInstanceIcon(icon);
            _replaceInstanceIcon = true;
            _removeInstanceIcon = false;
            InstanceEditorError = string.Empty;
        }
        catch (Exception)
        {
            if (IsEditingInstance && generation == _instanceIconPickerGeneration)
                InstanceEditorError = _localizer["instances.editor.invalidIcon"];
        }
        finally
        {
            if (generation == _instanceIconPickerGeneration)
                IsChoosingInstanceIcon = false;
        }
    }

    [RelayCommand]
    private void ResetInstanceIcon()
    {
        if (!IsEditingInstance || !HasEditInstanceIcon || IsChoosingInstanceIcon)
            return;

        ReplaceEditInstanceIcon(null);
        _removeInstanceIcon = true;
        _replaceInstanceIcon = false;
    }

    [RelayCommand]
    private void SaveManagedInstanceEdit()
    {
        if (!IsEditingInstance || _editingInstanceId is not { } instanceId ||
            _managedInstance?.Id != instanceId || !CanEditManagedInstance)
            return;

        var name = EditInstanceName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 64 || (EditInstanceNotes?.Length ?? 0) > 2000)
        {
            InstanceEditorError = _localizer["instances.editor.invalidName"];
            return;
        }

        var path = _instances.GetInstancePathById(instanceId);
        var meta = path is null ? null : _instances.GetInstanceMeta(path);
        if (meta is null)
        {
            InstanceEditorError = _localizer["instances.editor.saveFailed"];
            return;
        }

        try
        {
            meta.Name = name;
            meta.Notes = string.IsNullOrWhiteSpace(EditInstanceNotes) ? null : EditInstanceNotes.Trim();
            _instances.SaveInstanceMeta(path!, meta);
            var saved = _instances.GetInstanceMeta(path!);
            if (saved?.Name != meta.Name || saved.Notes != meta.Notes)
                throw new IOException("Instance metadata was not saved.");

            var iconPath = Path.Combine(path!, "logo.png");
            if (_replaceInstanceIcon && EditInstanceIcon is { } icon)
            {
                var temporaryPath = Path.Combine(path!, $"logo-{Guid.NewGuid():N}.png");
                try
                {
                    icon.Save(temporaryPath, PngBitmapEncoderOptions.Default);
                    File.Move(temporaryPath, iconPath, true);
                    DeleteLegacyInstanceIcons(path!);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            else if (_removeInstanceIcon)
            {
                if (File.Exists(iconPath))
                    File.Delete(iconPath);
                DeleteLegacyInstanceIcons(path!);
            }

            _instances.SyncInstancesWithConfig();
            RebuildInstancesFromCache();
            CancelEditManagedInstance();
        }
        catch (Exception)
        {
            InstanceEditorError = _localizer["instances.editor.saveFailed"];
        }
    }

    private static Bitmap? LoadInstanceIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var iconPath = new[] { "Icon.png", "logo.png", "icon.png" }
            .Select(name => Path.Combine(path, name))
            .FirstOrDefault(File.Exists);
        if (iconPath is null)
            return null;

        try
        {
            using var stream = File.OpenRead(iconPath);
            return DecodeInstanceIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap DecodeInstanceIcon(Stream stream)
    {
        var icon = new Bitmap(stream);
        var size = icon.PixelSize;
        if (Math.Max(size.Width, size.Height) <= MaxSavedInstanceIconDimension)
            return icon;

        icon.Dispose();
        stream.Position = 0;
        return size.Width >= size.Height
            ? Bitmap.DecodeToWidth(stream, MaxSavedInstanceIconDimension, BitmapInterpolationMode.HighQuality)
            : Bitmap.DecodeToHeight(stream, MaxSavedInstanceIconDimension, BitmapInterpolationMode.HighQuality);
    }

    private static void DeleteLegacyInstanceIcons(string path)
    {
        foreach (var name in new[] { "Icon.png", "icon.png" })
        {
            var iconPath = Path.Combine(path, name);
            if (File.Exists(iconPath))
                File.Delete(iconPath);
        }
    }

    private void ReplaceEditInstanceIcon(Bitmap? icon)
    {
        var previous = EditInstanceIcon;
        EditInstanceIcon = icon;
        previous?.Dispose();
    }

    [RelayCommand]
    private void RequestManagedInstanceDeletion()
    {
        if (_managedInstance is not { } instance || !CanDeleteManagedInstance)
            return;

        InstanceDeletionError = string.Empty;
        PendingManagedInstanceDeletion = instance;
        IsManagedInstanceDeletionOpen = true;
    }

    [RelayCommand]
    private void CancelManagedInstanceDeletion()
    {
        IsManagedInstanceDeletionOpen = false;
    }

    public void CompleteManagedInstanceDeletionClose()
    {
        PendingManagedInstanceDeletion = null;
        InstanceDeletionError = string.Empty;
    }

    [RelayCommand]
    private void ConfirmManagedInstanceDeletion()
    {
        var instance = PendingManagedInstanceDeletion;
        if (instance is null)
            return;

        if (!CanDeleteManagedInstance ||
            !string.Equals(_managedInstance?.Id, instance.Id, StringComparison.OrdinalIgnoreCase))
        {
            InstanceDeletionError = _localizer["instances.deleteFailed"];
            return;
        }

        bool deleted;
        var wasSuppressingInstancesChanged = _suppressInstancesChanged;
        _suppressInstancesChanged = true;
        try
        {
            deleted = _instances.DeleteGameById(instance.Id);
        }
        catch
        {
            deleted = false;
        }
        finally
        {
            _suppressInstancesChanged = wasSuppressingInstancesChanged;
        }

        if (!deleted)
        {
            InstanceDeletionError = _localizer["instances.deleteFailed"];
            return;
        }

        IsManagedInstanceDeletionOpen = false;
        _managedInstance = null;
        Volatile.Write(ref _logsInstanceId, null);
        InvalidateLogsRebuild();
        InstanceSection = string.Empty;
        RebuildInstancesFromCache();
        RefreshManagedInstanceContent();
    }

    [RelayCommand]
    private void CancelActivity()
    {
        var instanceId = _managedInstanceActionInstanceId ?? _selectedInstance?.Id;
        if (!string.IsNullOrWhiteSpace(instanceId))
            _installationWorkflow.CancelDownload(instanceId);
    }

    private bool IsInstanceBusy(string instanceId)
        => !string.IsNullOrWhiteSpace(instanceId)
        && _busyInstanceCounts.TryGetValue(instanceId, out var count)
        && count > 0;

    private void BeginInstanceActivity(string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            _busyInstanceCounts.TryGetValue(instanceId, out var currentCount);
            _busyInstanceCounts[instanceId] = currentCount + 1;
        }
        IsBusy = _busyInstanceCounts.Count > 0;
        UpdateSelectedInstancePresentation();
        NotifyManagedInstanceActionStateChanged();
    }

    private void EndInstanceActivity(string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            if (_busyInstanceCounts.TryGetValue(instanceId, out var currentCount))
            {
                if (currentCount <= 1)
                    _busyInstanceCounts.Remove(instanceId);
                else
                    _busyInstanceCounts[instanceId] = currentCount - 1;
            }
        }
        IsBusy = _busyInstanceCounts.Count > 0;
        UpdateSelectedInstancePresentation();
        NotifyManagedInstanceActionStateChanged();
    }

    private void OnInstancesChanged()
    {
        // Raised synchronously from repository mutations; skip when the change was
        // triggered by our own resync, which rebuilds right after it returns
        if (_suppressInstancesChanged)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                RebuildInstancesFromCache();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        });
    }

    private void RefreshInstances()
    {
        try
        {
            _suppressInstancesChanged = true;
            try
            {
                _instances.SyncInstancesWithConfig();
            }
            finally
            {
                _suppressInstancesChanged = false;
            }

            RebuildInstancesFromCache();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void RebuildInstancesFromCache()
    {
        var oldIcons = _allInstances.Select(item => item.Icon).ToArray();
        var items = _instances.GetCachedInstances();
        if (PendingManagedInstanceDeletion is { } pendingDeletion &&
            !items.Any(instance => string.Equals(
                instance.Id, pendingDeletion.Id, StringComparison.OrdinalIgnoreCase)))
            CancelManagedInstanceDeletion();

        var selectedInstanceId = _instances.GetSelectedInstance()?.Id;
        var requestedManagedInstanceId = _managedInstance?.Id;
        var managedInstance = items.FirstOrDefault(instance =>
                string.Equals(instance.Id, requestedManagedInstanceId, StringComparison.Ordinal))
            ?? items.FirstOrDefault();
        var managedInstanceId = managedInstance?.Id;

        var presentedInstances = items
            .Select(instance =>
            {
                RefreshInstanceInstalledState(instance);

                return new InstanceItemViewModel(
                    instance.Id,
                    FormatInstanceName(instance.Name, instance.Version, instance.VersionName),
                    FormatVersion(instance.Version, instance.VersionName),
                    FormatBranch(instance.Branch),
                    instance.IsInstalled,
                    string.Equals(instance.Id, managedInstanceId, StringComparison.Ordinal),
                    LoadInstanceIcon(_instances.GetInstancePathById(instance.Id) ?? string.Empty));
            })
            .ToList();

        _allInstances.ReplaceRange(presentedInstances);
        foreach (var icon in oldIcons)
            icon?.Dispose();

        _selectedInstance = items.FirstOrDefault(instance =>
            string.Equals(instance.Id, selectedInstanceId, StringComparison.Ordinal));
        _managedInstance = managedInstance;

        OnPropertyChanged(nameof(HasInstances));
        OnPropertyChanged(nameof(HasSelectedInstance));
        OnPropertyChanged(nameof(HasManagedInstance));

        if (_selectedInstance is not null)
            RefreshInstanceInstalledState(_selectedInstance);

        if (_managedInstance is not null)
            RefreshInstanceInstalledState(_managedInstance);

        UpdateSelectedInstancePresentation();
        UpdateManagedInstancePresentation();
    }

    private void RefreshInstanceInstalledState(InstanceInfo instance)
    {
        var path = _instances.GetInstancePathById(instance.Id);
        instance.IsInstalled =
            !string.IsNullOrWhiteSpace(path) && _instances.IsClientPresent(path);
    }

    private void RefreshManagedInstanceContent()
    {
        InstalledMods.Clear();
        VisibleInstalledMods.Clear();
        DisposeModCatalogInstallItems();
        foreach (var item in ModCatalogItems)
            item.Dispose();
        ModCatalogItems.Clear();
        ResetModCatalogPreview();
        InstanceWorlds.Clear();
        _modsLoadedForInstanceId = null;
        _worldsLoadedForInstanceId = null;
        _modUpdatesById.Clear();
        ModUpdateCount = 0;
        SelectedModCount = 0;
        InstanceContentError = string.Empty;
        RestartModIconFetch();
        NotifyInstanceContentCollectionsChanged();

        if (_managedInstance?.IsInstalled != true)
            return;

        _ = LoadInstalledModsAsync();
        _ = LoadInstanceWorldsAsync();

        if (IsInstanceBrowseSection)
            _ = SearchModCatalogAsync();
    }

    private async Task LoadInstalledModsAsync()
    {
        if (_modManager is null || _managedInstance?.IsInstalled != true)
            return;

        var instanceId = _managedInstance.Id;
        var instancePath = _instances.GetInstancePathById(instanceId);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsInstanceModsLoading = true;
        InstanceContentError = string.Empty;
        try
        {
            var mods = await Task.Run(() => _modManager.GetInstanceInstalledMods(instancePath));
            if (!string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
                return;

            UnsubscribeInstalledModItems();
            _installedMods.ReplaceRange(mods
                .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(mod =>
                {
                    var item = new InstanceModItemViewModel(
                        mod.Id,
                        mod.Name,
                        string.IsNullOrWhiteSpace(mod.Version) ? _localizer["common.unknown"] : mod.Version,
                        string.IsNullOrWhiteSpace(mod.Author) ? _localizer["common.unknown"] : mod.Author,
                        mod.Enabled,
                        mod.IconUrl,
                        mod.CurseForgeId,
                        mod.ReleaseType);
                    if (_modUpdatesById.TryGetValue(mod.Id, out var update))
                        item.UpdateVersion = update.LatestVersion;
                    item.PropertyChanged += OnInstalledModItemPropertyChanged;
                    return item;
                }));

            FilterInstalledMods();
            RefreshCatalogInstalledState(mods);
            FetchInstalledModIcons(_installedMods);
            RecalculateInstalledModsSelection();
            _modsLoadedForInstanceId = instanceId;
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            IsInstanceModsLoading = false;
        }
    }

    private async Task LoadModCatalogAsync(string query, bool append)
    {
        if (_modManager is null || _managedInstance?.IsInstalled != true)
            return;

        var instanceId = _managedInstance.Id;
        if (append)
            IsLoadingMoreModCatalog = true;
        else
        {
            IsModCatalogLoading = true;
            ResetModCatalogPreview();
        }
        InstanceContentError = string.Empty;
        try
        {
            var page = append ? _modCatalogPage + 1 : 0;
            var categories = SelectedModCatalogCategory is { Value: var categoryValue } &&
                             !string.IsNullOrWhiteSpace(categoryValue) &&
                             categoryValue != "all"
                ? new[] { categoryValue }
                : [];
            var sortField = int.TryParse(SelectedModCatalogSort?.Value, out var parsedSort)
                ? parsedSort
                : 2;
            var result = await _modManager.SearchModsAsync(
                query.Trim(),
                page,
                ModCatalogPageSize,
                categories,
                sortField,
                1);
            if (!string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
                return;

            var instancePath = _instances.GetInstancePathById(instanceId);
            var installed = string.IsNullOrWhiteSpace(instancePath)
                ? []
                : _modManager.GetInstanceInstalledMods(instancePath);
            _modCatalogGameVersion = string.IsNullOrWhiteSpace(instancePath)
                ? null
                : ModCompatibilityEvaluator.DetectInstanceGameVersion(instancePath);
            OnPropertyChanged(nameof(ModCatalogGameVersionLabel));
            var items = result.Mods.Select(mod =>
            {
                var installedMod = FindInstalledCatalogMod(mod.Id, installed);
                var recommendedFile = ModCompatibilityEvaluator.SelectRecommendedFile(
                    mod.LatestFiles,
                    _modCatalogGameVersion);
                var compatibility = recommendedFile is not null
                    ? ModCompatibilityEvaluator.Evaluate(
                        _modCatalogGameVersion,
                        recommendedFile.GameVersions)
                    : mod.LatestFiles.Count > 0
                        ? ModCompatibilityStatus.Incompatible
                        : ModCompatibilityStatus.Unknown;
                return new ModCatalogItemViewModel(
                    mod.Id,
                    mod.Name,
                    string.IsNullOrWhiteSpace(mod.Author) ? _localizer["common.unknown"] : mod.Author,
                    mod.Summary,
                    mod.LatestFileId,
                    mod.Slug,
                    mod.IconUrl,
                    mod.DownloadCount,
                    screenshotUrls: mod.Screenshots
                        .Select(screenshot => screenshot.Url ?? screenshot.ThumbnailUrl ?? string.Empty)
                        .Where(url => !string.IsNullOrWhiteSpace(url))
                        .ToList(),
                    installedFileId: installedMod?.FileId ?? string.Empty,
                    recommendedFileId: recommendedFile?.Id ??
                        (mod.LatestFiles.Count == 0 ? mod.LatestFileId : string.Empty),
                    compatibility: compatibility,
                    compatibilityLabel: GetModCompatibilityLabel(compatibility),
                    authorAvatarUrl: mod.AuthorAvatarUrl,
                    recommendedVersionLabel: recommendedFile is not null
                        ? string.IsNullOrWhiteSpace(recommendedFile.DisplayName)
                            ? recommendedFile.FileName
                            : recommendedFile.DisplayName
                        : mod.LatestFileId,
                    dependencies: recommendedFile?.Dependencies)
                {
                    IsInstalled = installedMod is not null
                };
            })
            .Where(item => !item.IsInstalled)
            .ToList();

            if (append)
                _modCatalogItems.AddRange(items);
            else
            {
                RestartModIconFetch();
                foreach (var existingItem in _modCatalogItems)
                    existingItem.Dispose();
                _modCatalogItems.ReplaceRange(items);
            }

            _modCatalogPage = page;
            HasMoreModCatalog = _modCatalogItems.Count < result.TotalCount && result.Mods.Count > 0;
            FetchCatalogModIcons(items);
            NotifyInstanceContentCollectionsChanged();
            NotifyCatalogSelectionChanged();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            IsModCatalogLoading = false;
            IsLoadingMoreModCatalog = false;
        }
    }

    private void FilterInstalledMods()
    {
        var query = InstalledModsSearchQuery.Trim();
        _visibleInstalledMods.ReplaceRange(InstalledMods.Where(mod =>
            string.IsNullOrWhiteSpace(query) ||
            mod.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            mod.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        NotifyInstanceContentCollectionsChanged();
    }

    private async Task LoadInstanceWorldsAsync()
    {
        if (_managedInstance?.IsInstalled != true)
            return;

        var instanceId = _managedInstance.Id;
        var instancePath = _instances.GetInstancePathById(instanceId);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsInstanceWorldsLoading = true;
        InstanceContentError = string.Empty;
        try
        {
            var worlds = await Task.Run(() => ReadInstanceWorlds(instancePath));
            if (!string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
                return;

            _instanceWorlds.ReplaceRange(worlds);
            _worldsLoadedForInstanceId = instanceId;
            NotifyInstanceContentCollectionsChanged();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            IsInstanceWorldsLoading = false;
        }
    }

    private IReadOnlyList<InstanceWorldItemViewModel> ReadInstanceWorlds(string instancePath)
    {
        var savesPath = Path.Combine(instancePath, "UserData", "Saves");
        if (!Directory.Exists(savesPath))
            return [];

        return Directory.EnumerateDirectories(savesPath)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .Select(directory => new InstanceWorldItemViewModel(
                directory.Name,
                directory.LastWriteTime.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
                FormatBytes(GetDirectorySize(directory))))
            .ToList();
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var displayValue = (double)value;
        while (displayValue >= 1024 && unitIndex < units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        return $"{displayValue:0.#} {units[unitIndex]}";
    }

    private void RefreshCatalogInstalledState(IReadOnlyCollection<InstalledMod> installedMods)
    {
        for (var index = ModCatalogItems.Count - 1; index >= 0; index--)
        {
            var item = ModCatalogItems[index];
            var installedMod = FindInstalledCatalogMod(item.Id, installedMods);
            if (installedMod is not null)
            {
                var isInstallSnapshotItem = _modCatalogInstallItems.Any(installItem =>
                    ReferenceEquals(installItem.CatalogItem, item));
                if (!isInstallSnapshotItem)
                    item.Dispose();
                ModCatalogItems.RemoveAt(index);
                continue;
            }

            item.IsInstalled = false;
        }

        NotifyCatalogSelectionChanged();
    }

    private void PrepareModCatalogInstallItems()
    {
        DisposeModCatalogInstallItems();
        RestartModDependencyIconFetch();
        foreach (var item in ModCatalogItems.Where(item => item.IsSelected && item.CanSelect))
        {
            _modCatalogInstallItems.Add(new ModCatalogInstallItemViewModel(
                item,
                count => _localizer.Format("modManager.dependsOnMods", count),
                _localizer["common.unknown"]));
        }
    }

    private void DisposeModCatalogInstallItems()
    {
        _modDependencyIconsCancellation.Cancel();
        foreach (var installItem in _modCatalogInstallItems)
        {
            installItem.Dispose();
            if (!ModCatalogItems.Contains(installItem.CatalogItem))
                installItem.CatalogItem.Dispose();
        }

        _modCatalogInstallItems.Clear();
    }

    private void RestartModDependencyIconFetch()
    {
        _modDependencyIconsCancellation.Cancel();
        _modDependencyIconsCancellation.Dispose();
        _modDependencyIconsCancellation = new CancellationTokenSource();
    }

    private static InstalledMod? FindInstalledCatalogMod(
        string catalogId,
        IEnumerable<InstalledMod> installedMods)
        => installedMods.FirstOrDefault(mod =>
            string.Equals(mod.CurseForgeId, catalogId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mod.Id, catalogId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mod.Id, $"cf-{catalogId}", StringComparison.OrdinalIgnoreCase));

    private void NotifyInstanceContentCollectionsChanged()
    {
        OnPropertyChanged(nameof(InstanceModsCountText));
        OnPropertyChanged(nameof(InstanceWorldsCountText));
        OnPropertyChanged(nameof(HasInstalledMods));
        OnPropertyChanged(nameof(HasModCatalogItems));
        OnPropertyChanged(nameof(HasInstanceWorlds));
        OnPropertyChanged(nameof(IsInstalledModsEmpty));
        OnPropertyChanged(nameof(IsModCatalogEmpty));
        OnPropertyChanged(nameof(IsInstanceWorldsEmpty));
        OnPropertyChanged(nameof(InstanceModsFooterText));
        OnPropertyChanged(nameof(HasModSelection));
        OnPropertyChanged(nameof(SelectedModCountText));
    }

    private async Task LoadInstanceVersionsAsync(string branch)
    {
        CancelInstanceVersionLoading();

        if (_versionCatalog is not null &&
            _versionCatalog.TryGetCachedVersionEntries(branch, InstanceVersionCacheMaxAge, out var cachedVersionEntries))
        {
            IsInstanceVersionsLoading = false;
            ApplyAvailableInstanceVersions(cachedVersionEntries);
            return;
        }

        if (_versionCatalog is not null &&
            _versionCatalog.TryGetCachedVersions(branch, InstanceVersionCacheMaxAge, out var cachedVersions))
        {
            IsInstanceVersionsLoading = false;
            ApplyAvailableInstanceVersions(cachedVersions);
            return;
        }

        _instanceVersionsCancellation = new CancellationTokenSource();
        var cancellationToken = _instanceVersionsCancellation.Token;

        IsInstanceVersionsLoading = true;
        SelectedNewInstanceVersion = null;
        AvailableInstanceVersions.Clear();

        try
        {
            if (_versionCatalog is null)
            {
                InstanceCreationError = "The version catalog is unavailable";
                return;
            }

            var versionResponse = await _versionCatalog.GetVersionListWithSourcesAsync(branch, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            if (versionResponse?.Versions.Count > 0)
            {
                ApplyAvailableInstanceVersions(versionResponse.Versions);
            }
            else
            {
                var versions = await _versionCatalog.GetVersionListAsync(branch, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    return;

                ApplyAvailableInstanceVersions(versions);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            InstanceCreationError = ex.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsInstanceVersionsLoading = false;
                OnPropertyChanged(nameof(HasAvailableInstanceVersions));
            }
        }
    }

    private void ResetInstanceCreatorState()
    {
        CancelInstanceVersionLoading();
        NewInstanceBranch = "release";
        SelectedNewInstanceVersion = null;
        AvailableInstanceVersions.Clear();
        IsInstanceVersionsLoading = false;
        InstanceCreationError = string.Empty;
        OnPropertyChanged(nameof(HasAvailableInstanceVersions));
    }

    private void CancelInstanceVersionLoading()
    {
        _instanceVersionsCancellation?.Cancel();
        _instanceVersionsCancellation?.Dispose();
        _instanceVersionsCancellation = null;
    }

    private void ApplyAvailableInstanceVersions(IReadOnlyList<int> versions)
    {
        SelectedNewInstanceVersion = null;
        var orderedVersions = versions
            .OrderByDescending(version => version)
            .Take(12)
            .ToList();
        var selectedVersion = orderedVersions.FirstOrDefault();
        _availableInstanceVersions.ReplaceRange(orderedVersions
            .Select(version => new InstanceVersionItemViewModel(
                version,
                IsSelected: version == selectedVersion)));

        SelectedNewInstanceVersion = AvailableInstanceVersions.FirstOrDefault();
        OnPropertyChanged(nameof(HasAvailableInstanceVersions));
    }

    private void ApplyAvailableInstanceVersions(IReadOnlyList<CachedVersionEntry> versions)
    {
        SelectedNewInstanceVersion = null;
        var orderedVersions = versions
            .OrderBy(version => IsLegacyBuildVersionName(version.VersionName))
            .Take(12)
            .ToList();
        var selectedVersion = orderedVersions.FirstOrDefault();
        _availableInstanceVersions.ReplaceRange(orderedVersions
            .Select(version => new InstanceVersionItemViewModel(
                version.Version,
                version.VersionName,
                isSelected: version.Version == selectedVersion?.Version)));

        SelectedNewInstanceVersion = AvailableInstanceVersions.FirstOrDefault();
        OnPropertyChanged(nameof(HasAvailableInstanceVersions));
    }

    private void ApplyAvailableInstanceVersions(IReadOnlyList<VersionInfo> versions)
    {
        SelectedNewInstanceVersion = null;
        var orderedVersions = versions
            .OrderBy(version => IsLegacyBuildVersionName(version.VersionName))
            .Take(12)
            .ToList();
        var selectedVersion = orderedVersions.FirstOrDefault();
        _availableInstanceVersions.ReplaceRange(orderedVersions
            .Select(version => new InstanceVersionItemViewModel(
                version.Version,
                version.VersionName,
                isSelected: version.Version == selectedVersion?.Version)));

        SelectedNewInstanceVersion = AvailableInstanceVersions.FirstOrDefault();
        OnPropertyChanged(nameof(HasAvailableInstanceVersions));
    }

    private static bool IsLegacyBuildVersionName(string? versionName)
        => !string.IsNullOrWhiteSpace(versionName) &&
           versionName.TrimStart().StartsWith("build", StringComparison.OrdinalIgnoreCase);

    private void RefreshAvailableInstanceVersionSelection(int selectedVersion)
    {
        for (var index = 0; index < AvailableInstanceVersions.Count; index++)
        {
            var option = AvailableInstanceVersions[index];
            AvailableInstanceVersions[index] = option with { IsSelected = option.Version == selectedVersion };
        }

        SelectedNewInstanceVersion = AvailableInstanceVersions
            .FirstOrDefault(option => option.Version == selectedVersion);
    }
    private void UpdateSelectedInstancePresentation()
    {
        OnPropertyChanged(nameof(IsSelectedInstanceInstalled));
        OnPropertyChanged(nameof(IsInstalledModsEmpty));
        OnPropertyChanged(nameof(IsModCatalogEmpty));
        OnPropertyChanged(nameof(IsInstanceWorldsEmpty));

        if (_selectedInstance is null)
        {
            SelectedInstanceName = SelectInstanceLabel;
            SelectedInstanceMeta = _localizer["instances.noInstances"];
            SelectedInstanceState = _localizer["instances.status.unknown"];
            SelectedInstanceBranch = string.Empty;
            SelectedInstanceVersion = string.Empty;
            SelectedInstancePlayTime = FormatPlayTime(0);
            return;
        }

        SelectedInstanceName = FormatInstanceName(
            _selectedInstance.Name,
            _selectedInstance.Version,
            _selectedInstance.VersionName);
        SelectedInstanceMeta = $"{FormatBranch(_selectedInstance.Branch)}  ·  {FormatVersion(_selectedInstance.Version, _selectedInstance.VersionName)}";
        SelectedInstanceBranch = FormatBranch(_selectedInstance.Branch);
        SelectedInstanceVersion = FormatVersion(_selectedInstance.Version, _selectedInstance.VersionName);
        SelectedInstancePlayTime = FormatPlayTime(GetSelectedInstancePlayTimeSeconds());
        SelectedInstanceState = _selectedInstance.IsInstalled
            ? _localizer["instances.status.ready"]
            : _localizer["instances.status.notInstalled"];
    }

    private void UpdateManagedInstancePresentation()
    {
        ManagedInstanceIcon = _allInstances.FirstOrDefault(item => item.Id == _managedInstance?.Id)?.Icon;
        OnPropertyChanged(nameof(IsManagedInstanceInstalled));
        OnPropertyChanged(nameof(CanRunManagedInstanceAction));
        OnPropertyChanged(nameof(CanOpenManagedInstanceFolder));
        OnPropertyChanged(nameof(CanDeleteManagedInstance));
        OnPropertyChanged(nameof(CanEditManagedInstance));
        OnPropertyChanged(nameof(ManagedInstanceActionLabel));
        NotifyManagedInstanceActionStateChanged();
        OnPropertyChanged(nameof(ManagedInstanceDeleteHint));
        OnPropertyChanged(nameof(IsInstalledModsEmpty));
        OnPropertyChanged(nameof(IsModCatalogEmpty));
        OnPropertyChanged(nameof(IsInstanceWorldsEmpty));
        OnPropertyChanged(nameof(InstanceSectionTitle));

        if (_managedInstance is null)
        {
            ManagedInstanceName = string.Empty;
            ManagedInstanceState = _localizer["instances.status.unknown"];
            ManagedInstanceBranch = string.Empty;
            ManagedInstanceVersion = string.Empty;
            ManagedInstancePlayTime = FormatPlayTime(0);
            ManagedInstanceNotes = string.Empty;
            return;
        }

        ManagedInstanceName = FormatInstanceName(
            _managedInstance.Name,
            _managedInstance.Version,
            _managedInstance.VersionName);
        ManagedInstanceBranch = FormatBranch(_managedInstance.Branch);
        ManagedInstanceVersion = FormatVersion(_managedInstance.Version, _managedInstance.VersionName);
        ManagedInstancePlayTime = FormatPlayTime(GetManagedInstancePlayTimeSeconds());
        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        ManagedInstanceNotes = instancePath is null
            ? string.Empty
            : _instances.GetInstanceMeta(instancePath)?.Notes ?? string.Empty;
        ManagedInstanceState = _managedInstance.IsInstalled
            ? _localizer["instances.status.ready"]
            : _localizer["instances.status.notInstalled"];
    }
    private string FormatVersion(int version, string? versionName)
        => version <= 0
            ? _localizer["common.unknown"]
            : string.IsNullOrWhiteSpace(versionName) ? version.ToString() : versionName;

    private string FormatInstanceName(string name, int version, string? versionName)
        => string.Equals(name, InstanceMeta.DefaultName, StringComparison.Ordinal)
            ? $"{InstanceMeta.DefaultName} {FormatVersion(version, versionName)}"
            : name;

    private string FormatBranch(string branch)
        => branch.Contains("pre", StringComparison.OrdinalIgnoreCase)
            ? _localizer["common.preRelease"]
            : _localizer["common.release"];

    private long GetSelectedInstancePlayTimeSeconds()
    {
        if (_selectedInstance is null)
            return 0;

        var instancePath = _instances.GetInstancePathById(_selectedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return 0;

        return Math.Max(0, _instances.GetInstanceMeta(instancePath)?.PlayTimeSeconds ?? 0);
    }

    private long GetManagedInstancePlayTimeSeconds()
    {
        if (_managedInstance is null)
            return 0;

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return 0;

        return Math.Max(0, _instances.GetInstanceMeta(instancePath)?.PlayTimeSeconds ?? 0);
    }

    private string FormatPlayTime(long seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var totalHours = (long)duration.TotalHours;
        return _localizer.Format("instances.info.playtimeValue", totalHours, duration.Minutes);
    }

    private string FormatManagedInstanceActionElapsedTime()
    {
        var startedAt = IsManagedInstanceActionRunning
            ? _managedInstanceGameStartedAtUtc ?? _gameProcess.GetRunningProcesses()
                .FirstOrDefault(process => string.Equals(
                    process.InstanceId,
                    _managedInstance?.Id,
                    StringComparison.OrdinalIgnoreCase))
                ?.ProcessStartedAtUtc
            : _managedInstanceActionStartedAtUtc;
        if (startedAt is null)
            return "0:00";

        var duration = DateTime.UtcNow - startedAt.Value;
        var totalHours = (long)duration.TotalHours;
        return totalHours > 0
            ? $"{totalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private void OnManagedInstanceActionTimerTick(object? sender, EventArgs args)
        => OnPropertyChanged(nameof(ManagedInstanceActionMetricText));

    private void NotifyManagedInstanceActionStateChanged()
    {
        OnPropertyChanged(nameof(IsManagedInstanceActionActive));
        OnPropertyChanged(nameof(IsManagedInstanceActionRunning));
        OnPropertyChanged(nameof(ShouldSpinManagedInstanceAction));
        OnPropertyChanged(nameof(IsManagedInstanceCancellationArmed));
        OnPropertyChanged(nameof(CanRunManagedInstanceAction));
        OnPropertyChanged(nameof(CanDeleteManagedInstance));
        OnPropertyChanged(nameof(CanEditManagedInstance));
        OnPropertyChanged(nameof(IsManagedInstanceEditActionCollapsed));
        OnPropertyChanged(nameof(ManagedInstanceActionStatusText));
        OnPropertyChanged(nameof(ManagedInstanceActionMetricText));
    }

    private void EndManagedInstanceAction()
    {
        _managedInstanceActionTimer.Stop();
        _managedInstanceActionStartedAtUtc = null;
        _managedInstanceGameStartedAtUtc = null;
        _managedInstanceActionInstanceId = null;
        _managedInstanceActionStartedWithInstall = false;
        _isManagedInstanceCancellationArmed = false;
        NotifyManagedInstanceActionStateChanged();
    }

    public void ArmManagedInstanceCancellation()
    {
        if (!IsManagedInstanceActionActive || _isManagedInstanceCancellationArmed)
            return;

        _isManagedInstanceCancellationArmed = true;
        OnPropertyChanged(nameof(IsManagedInstanceCancellationArmed));
    }

    private void OnDownloadProgressChanged(ProgressUpdateMessage update)
    {
        Interlocked.Exchange(ref _pendingProgressUpdate, update);
        SchedulePendingProgressUpdate();
    }

    private void SchedulePendingProgressUpdate()
    {
        if (Interlocked.CompareExchange(ref _progressUpdateScheduled, 1, 0) != 0)
            return;

        Dispatcher.UIThread.Post(ApplyPendingProgressUpdate, DispatcherPriority.Background);
    }

    private void ApplyPendingProgressUpdate()
    {
        var update = Interlocked.Exchange(ref _pendingProgressUpdate, null);
        if (update is not null)
        {
            var activeInstanceId = _managedInstanceActionInstanceId ?? _selectedInstance?.Id;
            if (IsBusy && (update.InstanceId is null ||
                           string.Equals(update.InstanceId, activeInstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                ActivityProgress = Math.Clamp(update.Progress, 0, 100);
                ActivityProgressText = $"{ActivityProgress:0}%";
                ActivityTitle = update.Args is { Length: > 0 }
                    ? _localizer.Format(update.MessageKey, update.Args)
                    : _localizer[update.MessageKey];
                ActivityDetail = update.State;
                IsActivityVisible = true;
            }
        }

        Interlocked.Exchange(ref _progressUpdateScheduled, 0);
        if (Volatile.Read(ref _pendingProgressUpdate) is not null)
            SchedulePendingProgressUpdate();
    }

    private void OnGameProcessStarted(object? sender, GameProcessStartedEventArgs e)
    {
        var process = e.Process;
        Dispatcher.UIThread.Post(() =>
        {
            if (string.Equals(
                _managedInstanceActionInstanceId,
                process.InstanceId,
                StringComparison.OrdinalIgnoreCase))
            {
                _managedInstanceGameStartedAtUtc ??= DateTime.UtcNow;
            }

            IsGameRunning = _gameProcess.IsGameRunning();
            IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
            NotifyManagedInstanceActionStateChanged();
        });
    }

    private void OnGameProcessExited(object? sender, GameProcessExitedEventArgs e)
    {
        var process = e.Process;
        Dispatcher.UIThread.Post(() =>
        {
            var endsManagedAction = IsManagedInstanceActionActive &&
                string.Equals(
                    _managedInstanceActionInstanceId,
                    process.InstanceId,
                    StringComparison.OrdinalIgnoreCase);
            if (endsManagedAction)
                _completedManagedActivityGenerations.Add(_managedInstanceActionGeneration);
            CanCancelActivity = false;
            EndInstanceActivity(process.InstanceId);

            IsGameRunning = _gameProcess.IsGameRunning();
            IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
            if (endsManagedAction)
                EndManagedInstanceAction();
            else
                NotifyManagedInstanceActionStateChanged();
        });
    }

    private void OnLaunchFailed(object? sender, LaunchFailedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var instanceId = e.InstanceId
                ?? _managedInstanceActionInstanceId
                ?? _selectedInstance?.Id;
            var endsManagedAction = IsManagedInstanceActionActive &&
                string.Equals(
                    _managedInstanceActionInstanceId,
                    instanceId,
                    StringComparison.OrdinalIgnoreCase);
            if (endsManagedAction)
                _completedManagedActivityGenerations.Add(_managedInstanceActionGeneration);
            CanCancelActivity = false;
            if (!string.IsNullOrWhiteSpace(instanceId))
                EndInstanceActivity(instanceId);

            if (e.ExitCode == 0)
                IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
            if (endsManagedAction)
                EndManagedInstanceAction();
            else
                NotifyManagedInstanceActionStateChanged();
        });
    }

    private void OnOperationErrorOccurred(OperationErrorMessage error)
    {
        var activeInstanceId = _managedInstanceActionInstanceId ?? _selectedInstance?.Id;
        if (error.InstanceId is not null &&
            !string.Equals(error.InstanceId, activeInstanceId, StringComparison.OrdinalIgnoreCase))
            return;

        Dispatcher.UIThread.Post(
            () => ShowError(error.Technical ?? error.Message),
            DispatcherPriority.Normal);
    }

    private void ShowError(string message)
    {
        ActivityTitle = _localizer["error.title"];
        ActivityDetail = message;
        ActivityProgress = 0;
        ActivityProgressText = string.Empty;
        IsActivityVisible = true;
        CanCancelActivity = false;
        UpdateSelectedInstancePresentation();
    }
    partial void OnIsBusyChanged(bool value)
        => NotifyManagedInstanceActionStateChanged();

    partial void OnIsGameRunningChanged(bool value)
        => NotifyManagedInstanceActionStateChanged();

    partial void OnActivityTitleChanged(string value)
        => OnPropertyChanged(nameof(ManagedInstanceActionStatusText));

    partial void OnActivityProgressTextChanged(string value)
        => OnPropertyChanged(nameof(ManagedInstanceActionMetricText));

    partial void OnInstalledModsSearchQueryChanged(string value)
        => FilterInstalledMods();
    public void Dispose()
    {
        ReplaceEditInstanceIcon(null);
        foreach (var item in _allInstances)
            item.Icon?.Dispose();
        Interlocked.Exchange(ref _isDisposed, 1);
        Volatile.Write(ref _logsInstanceId, null);
        InvalidateLogsRebuild();
        _managedInstanceActionTimer.Stop();
        _managedInstanceActionTimer.Tick -= OnManagedInstanceActionTimerTick;
        if (_gameConsole is not null)
            _gameConsole.LineReceived -= OnConsoleLineReceived;
        UnsubscribeInstalledModItems();
        RestartModIconFetch();
        _modIconsCancellation.Dispose();
        _modPreviewImageCancellation.Cancel();
        _modPreviewImageCancellation.Dispose();
        _modPreviewImageTransitionCancellation.Cancel();
        _modPreviewImageTransitionCancellation.Dispose();
        _modPreviewRevealCancellation.Cancel();
        _modPreviewRevealCancellation.Dispose();
        DisposeModCatalogPreviewBitmaps();
        CancelInstanceVersionLoading();
        DisposeModCatalogInstallItems();
        _modDependencyIconsCancellation.Dispose();
        foreach (var item in ModCatalogItems)
            item.Dispose();
        Interlocked.Exchange(ref _pendingProgressUpdate, null);
        _progress.DownloadProgressChanged -= OnDownloadProgressChanged;
        _progress.OperationErrorOccurred -= OnOperationErrorOccurred;
        _gameProcess.GameProcessStarted -= OnGameProcessStarted;
        _gameProcess.GameProcessExited -= OnGameProcessExited;
        _gameLaunchCoordinator.LaunchFailed -= OnLaunchFailed;
        _instances.InstancesChanged -= OnInstancesChanged;
        _localizer.LanguageChanged -= ApplyLanguage;
    }

}
