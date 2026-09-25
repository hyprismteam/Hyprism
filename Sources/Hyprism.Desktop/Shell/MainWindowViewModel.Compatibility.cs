// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.News;

namespace Hyprism.Desktop.Shell;

public sealed partial class MainWindowViewModel
{
    public string SelectedInstanceName
    {
        get => Instances.SelectedInstanceName;
        set => Instances.SelectedInstanceName = value;
    }
    public string SelectedInstanceMeta
    {
        get => Instances.SelectedInstanceMeta;
        set => Instances.SelectedInstanceMeta = value;
    }
    public string SelectedInstanceState
    {
        get => Instances.SelectedInstanceState;
        set => Instances.SelectedInstanceState = value;
    }
    public string SelectedInstanceBranch
    {
        get => Instances.SelectedInstanceBranch;
        set => Instances.SelectedInstanceBranch = value;
    }
    public string SelectedInstanceVersion
    {
        get => Instances.SelectedInstanceVersion;
        set => Instances.SelectedInstanceVersion = value;
    }
    public string SelectedInstancePlayTime
    {
        get => Instances.SelectedInstancePlayTime;
        set => Instances.SelectedInstancePlayTime = value;
    }
    public string ManagedInstanceName
    {
        get => Instances.ManagedInstanceName;
        set => Instances.ManagedInstanceName = value;
    }
    public string ManagedInstanceState
    {
        get => Instances.ManagedInstanceState;
        set => Instances.ManagedInstanceState = value;
    }
    public string ManagedInstanceBranch
    {
        get => Instances.ManagedInstanceBranch;
        set => Instances.ManagedInstanceBranch = value;
    }
    public string ManagedInstanceVersion
    {
        get => Instances.ManagedInstanceVersion;
        set => Instances.ManagedInstanceVersion = value;
    }
    public string ManagedInstancePlayTime
    {
        get => Instances.ManagedInstancePlayTime;
        set => Instances.ManagedInstancePlayTime = value;
    }
    public bool CanCancelActivity
    {
        get => Instances.CanCancelActivity;
        set => Instances.CanCancelActivity = value;
    }
    public bool IsBusy
    {
        get => Instances.IsBusy;
        set => Instances.IsBusy = value;
    }
    public bool IsGameRunning
    {
        get => Instances.IsGameRunning;
        set => Instances.IsGameRunning = value;
    }
    public bool IsActivityVisible
    {
        get => Instances.IsActivityVisible;
        set => Instances.IsActivityVisible = value;
    }
    public double ActivityProgress
    {
        get => Instances.ActivityProgress;
        set => Instances.ActivityProgress = value;
    }
    public string ActivityProgressText
    {
        get => Instances.ActivityProgressText;
        set => Instances.ActivityProgressText = value;
    }
    public string ActivityTitle
    {
        get => Instances.ActivityTitle;
        set => Instances.ActivityTitle = value;
    }
    public string ActivityDetail
    {
        get => Instances.ActivityDetail;
        set => Instances.ActivityDetail = value;
    }
    public string NewInstanceBranch
    {
        get => Instances.NewInstanceBranch;
        set => Instances.NewInstanceBranch = value;
    }
    public bool IsInstanceVersionsLoading
    {
        get => Instances.IsInstanceVersionsLoading;
        set => Instances.IsInstanceVersionsLoading = value;
    }
    public InstanceVersionItemViewModel? SelectedNewInstanceVersion
    {
        get => Instances.SelectedNewInstanceVersion;
        set => Instances.SelectedNewInstanceVersion = value;
    }
    public bool IsInstanceCreatorOpen
    {
        get => Instances.IsInstanceCreatorOpen;
        set => Instances.IsInstanceCreatorOpen = value;
    }
    public string InstanceCreationError
    {
        get => Instances.InstanceCreationError;
        set => Instances.InstanceCreationError = value;
    }
    public string DisplayedInstanceSectionTitle
    {
        get => Instances.DisplayedInstanceSectionTitle;
        set => Instances.DisplayedInstanceSectionTitle = value;
    }
    public string InstanceSection
    {
        get => Instances.InstanceSection;
        set => Instances.InstanceSection = value;
    }
    public bool IsInstanceModsLoading
    {
        get => Instances.IsInstanceModsLoading;
        set => Instances.IsInstanceModsLoading = value;
    }
    public bool IsModCatalogLoading
    {
        get => Instances.IsModCatalogLoading;
        set => Instances.IsModCatalogLoading = value;
    }
    public bool IsInstanceWorldsLoading
    {
        get => Instances.IsInstanceWorldsLoading;
        set => Instances.IsInstanceWorldsLoading = value;
    }
    public string InstanceContentError
    {
        get => Instances.InstanceContentError;
        set => Instances.InstanceContentError = value;
    }
    public string InstalledModsSearchQuery
    {
        get => Instances.InstalledModsSearchQuery;
        set => Instances.InstalledModsSearchQuery = value;
    }
    public string ModCatalogSearchQuery
    {
        get => Instances.ModCatalogSearchQuery;
        set => Instances.ModCatalogSearchQuery = value;
    }
    public int SelectedModCount
    {
        get => Instances.SelectedModCount;
        set => Instances.SelectedModCount = value;
    }
    public int ModUpdateCount
    {
        get => Instances.ModUpdateCount;
        set => Instances.ModUpdateCount = value;
    }
    public bool IsCheckingModUpdates
    {
        get => Instances.IsCheckingModUpdates;
        set => Instances.IsCheckingModUpdates = value;
    }
    public bool IsApplyingModUpdates
    {
        get => Instances.IsApplyingModUpdates;
        set => Instances.IsApplyingModUpdates = value;
    }
    public string LogsSearchQuery
    {
        get => Instances.LogsSearchQuery;
        set => Instances.LogsSearchQuery = value;
    }
    public bool IsLogsAutoScroll
    {
        get => Instances.IsLogsAutoScroll;
        set => Instances.IsLogsAutoScroll = value;
    }
    public bool IsLogsWrap
    {
        get => Instances.IsLogsWrap;
        set => Instances.IsLogsWrap = value;
    }
    public bool IsLogsDebugEnabled { get => Instances.IsLogsDebugEnabled; set => Instances.IsLogsDebugEnabled = value; }
    public bool IsLogsWarningsEnabled { get => Instances.IsLogsWarningsEnabled; set => Instances.IsLogsWarningsEnabled = value; }
    public bool IsLogsErrorsEnabled { get => Instances.IsLogsErrorsEnabled; set => Instances.IsLogsErrorsEnabled = value; }
    public bool IsLogsTracingEnabled { get => Instances.IsLogsTracingEnabled; set => Instances.IsLogsTracingEnabled = value; }
    public bool IsLogsLevelPopupOpen { get => Instances.IsLogsLevelPopupOpen; set => Instances.IsLogsLevelPopupOpen = value; }
    public int LogsRevision
    {
        get => Instances.LogsRevision;
        set => Instances.LogsRevision = value;
    }
    public InstanceListOptionViewModel? SelectedModCatalogCategory
    {
        get => Instances.SelectedModCatalogCategory;
        set => Instances.SelectedModCatalogCategory = value;
    }
    public InstanceListOptionViewModel? SelectedModCatalogSort
    {
        get => Instances.SelectedModCatalogSort;
        set => Instances.SelectedModCatalogSort = value;
    }
    public bool IsLoadingMoreModCatalog
    {
        get => Instances.IsLoadingMoreModCatalog;
        set => Instances.IsLoadingMoreModCatalog = value;
    }
    public bool HasMoreModCatalog
    {
        get => Instances.HasMoreModCatalog;
        set => Instances.HasMoreModCatalog = value;
    }
    public ModCatalogItemViewModel? SelectedModCatalogPreview
    {
        get => Instances.SelectedModCatalogPreview;
        set => Instances.SelectedModCatalogPreview = value;
    }
    public bool IsModCatalogPreviewOpen
    {
        get => Instances.IsModCatalogPreviewOpen;
        set => Instances.IsModCatalogPreviewOpen = value;
    }
    public ModCatalogFileItemViewModel? SelectedModCatalogPreviewFile
    {
        get => Instances.SelectedModCatalogPreviewFile;
        set => Instances.SelectedModCatalogPreviewFile = value;
    }
    public Bitmap? ModCatalogPreviewImage
    {
        get => Instances.ModCatalogPreviewImage;
        set => Instances.ModCatalogPreviewImage = value;
    }
    public bool IsModCatalogPreviewLoading
    {
        get => Instances.IsModCatalogPreviewLoading;
        set => Instances.IsModCatalogPreviewLoading = value;
    }
    public bool IsModCatalogPreviewFilesSkeletonVisible
    {
        get => Instances.IsModCatalogPreviewFilesSkeletonVisible;
        set => Instances.IsModCatalogPreviewFilesSkeletonVisible = value;
    }
    public bool IsModCatalogPreviewFilesSkeletonFadingOut
    {
        get => Instances.IsModCatalogPreviewFilesSkeletonFadingOut;
        set => Instances.IsModCatalogPreviewFilesSkeletonFadingOut = value;
    }
    public bool IsModCatalogPreviewFilesContentVisible
    {
        get => Instances.IsModCatalogPreviewFilesContentVisible;
        set => Instances.IsModCatalogPreviewFilesContentVisible = value;
    }
    public bool IsModCatalogPreviewImageLoading
    {
        get => Instances.IsModCatalogPreviewImageLoading;
        set => Instances.IsModCatalogPreviewImageLoading = value;
    }
    public bool IsModCatalogPreviewImageVisible
    {
        get => Instances.IsModCatalogPreviewImageVisible;
        set => Instances.IsModCatalogPreviewImageVisible = value;
    }
    public bool IsModCatalogPreviewImageTransitioning
    {
        get => Instances.IsModCatalogPreviewImageTransitioning;
        set => Instances.IsModCatalogPreviewImageTransitioning = value;
    }
    public bool IsInstallingSelectedCatalogMods
    {
        get => Instances.IsInstallingSelectedCatalogMods;
        set => Instances.IsInstallingSelectedCatalogMods = value;
    }
    public bool IsModCatalogInstallConfirmationOpen
    {
        get => Instances.IsModCatalogInstallConfirmationOpen;
        set => Instances.IsModCatalogInstallConfirmationOpen = value;
    }
    public int ModCatalogPreviewScreenshotIndex
    {
        get => Instances.ModCatalogPreviewScreenshotIndex;
        set => Instances.ModCatalogPreviewScreenshotIndex = value;
    }
    public string SelectInstanceLabel => Instances.SelectInstanceLabel;
    public string VersionLabel => Instances.VersionLabel;
    public string BranchLabel => Instances.BranchLabel;
    public string ReleaseLabel => Instances.ReleaseLabel;
    public string PreReleaseLabel => Instances.PreReleaseLabel;
    public string InstancesSectionLabel => Instances.InstancesSectionLabel;
    public string SelectVersionLabel => Instances.SelectVersionLabel;
    public string CreateInstanceLabel => Instances.CreateInstanceLabel;
    public string CreateInstanceTitle => Instances.CreateInstanceTitle;
    public string NewInstanceTitle => Instances.NewInstanceTitle;
    public string NewInstanceHint => Instances.NewInstanceHint;
    public string CreateInstanceHint => Instances.CreateInstanceHint;
    public string InstanceBranchHint => Instances.InstanceBranchHint;
    public string InstanceVersionHint => Instances.InstanceVersionHint;
    public string CancelLabel => Instances.CancelLabel;
    public string InstanceModsLabel => Instances.InstanceModsLabel;
    public string InstanceBrowseLabel => Instances.InstanceBrowseLabel;
    public string InstanceWorldsLabel => Instances.InstanceWorldsLabel;
    public string InstanceModsTitle => Instances.InstanceModsTitle;
    public string InstanceModsHint => Instances.InstanceModsHint;
    public string InstanceModsSearchHint => Instances.InstanceModsSearchHint;
    public string InstanceModsEmptyTitle => Instances.InstanceModsEmptyTitle;
    public string InstanceModsEmptyHint => Instances.InstanceModsEmptyHint;
    public string InstanceBrowseTitle => Instances.InstanceBrowseTitle;
    public string InstanceBrowseHint => Instances.InstanceBrowseHint;
    public string InstanceBrowseSearchHint => Instances.InstanceBrowseSearchHint;
    public string InstanceBrowseEmptyTitle => Instances.InstanceBrowseEmptyTitle;
    public string InstanceBrowseEmptyHint => Instances.InstanceBrowseEmptyHint;
    public string InstanceWorldsTitle => Instances.InstanceWorldsTitle;
    public string InstanceWorldsHint => Instances.InstanceWorldsHint;
    public string InstanceWorldsEmptyTitle => Instances.InstanceWorldsEmptyTitle;
    public string InstanceWorldsEmptyHint => Instances.InstanceWorldsEmptyHint;
    public string InstanceLogsTitle => Instances.InstanceLogsTitle;
    public string InstanceLogsHint => Instances.InstanceLogsHint;
    public string InstanceLogsEmptyTitle => Instances.InstanceLogsEmptyTitle;
    public string InstanceLogsEmptyHint => Instances.InstanceLogsEmptyHint;
    public string InstanceModsCheckUpdatesLabel => Instances.InstanceModsCheckUpdatesLabel;
    public string InstanceModsUpdateAllLabel => Instances.InstanceModsUpdateAllLabel;
    public string InstanceModsAddLabel => Instances.InstanceModsAddLabel;
    public string InstanceModsDropImportLabel => Instances.InstanceModsDropImportLabel;
    public string InstanceModsSelectAllLabel => Instances.InstanceModsSelectAllLabel;
    public string InstanceModsClearSelectionLabel => Instances.InstanceModsClearSelectionLabel;
    public string InstanceModsEnableSelectedLabel => Instances.InstanceModsEnableSelectedLabel;
    public string InstanceModsDisableSelectedLabel => Instances.InstanceModsDisableSelectedLabel;
    public string InstanceModsDeleteSelectedLabel => Instances.InstanceModsDeleteSelectedLabel;
    public string InstanceModsDeleteTooltip => Instances.InstanceModsDeleteTooltip;
    public string InstanceModsDeleteLabel => Instances.InstanceModsDeleteLabel;
    public string InstanceModsDeleteTitle => Instances.InstanceModsDeleteTitle;
    public string InstanceModsDeleteHint => Instances.InstanceModsDeleteHint;
    public string InstanceModsOpenPageTooltip => Instances.InstanceModsOpenPageTooltip;
    public string InstanceModsToggleTooltip => Instances.InstanceModsToggleTooltip;
    public string InstanceBrowseCategoryLabel => Instances.InstanceBrowseCategoryLabel;
    public string InstanceBrowseSortLabel => Instances.InstanceBrowseSortLabel;
    public string InstanceBrowseLoadingMoreLabel => Instances.InstanceBrowseLoadingMoreLabel;
    public string ModCatalogPreviewAuthorLabel => Instances.ModCatalogPreviewAuthorLabel;
    public string ModCatalogPreviewDownloadsLabel => Instances.ModCatalogPreviewDownloadsLabel;
    public string ModCatalogPreviewNoFilesLabel => Instances.ModCatalogPreviewNoFilesLabel;
    public string ModCatalogPreviewCloseLabel => Instances.ModCatalogPreviewCloseLabel;
    public string ModCatalogOpenCurseForgeLabel => Instances.ModCatalogOpenCurseForgeLabel;
    public string ModCatalogFileTypeColumn => Instances.ModCatalogFileTypeColumn;
    public string ModCatalogFileNameColumn => Instances.ModCatalogFileNameColumn;
    public string ModCatalogFileGameVersionsColumn => Instances.ModCatalogFileGameVersionsColumn;
    public string ModCatalogInstallSelectedLabel => Instances.ModCatalogInstallSelectedLabel;
    public string ModCatalogInstallTitle => Instances.ModCatalogInstallTitle;
    public string ModCatalogInstallPreviewTitle => Instances.ModCatalogInstallPreviewTitle;
    public string ModCatalogInstallDependencyHint => Instances.ModCatalogInstallDependencyHint;
    public string ModCatalogInstallVersionColumn => Instances.ModCatalogInstallVersionColumn;
    public string ModCatalogInstallDependenciesColumn => Instances.ModCatalogInstallDependenciesColumn;
    public string ModCatalogGameVersionLabel => Instances.ModCatalogGameVersionLabel;
    public string LogsAutoScrollLabel => Instances.LogsAutoScrollLabel;
    public string LogsClearLabel => Instances.LogsClearLabel;
    public string LogsSearchHint => Instances.LogsSearchHint;
    public string LogsWrapLabel => Instances.LogsWrapLabel;
    public string LogsLevelLabel => Instances.LogsLevelLabel;
    public string LogsDebugLabel => Instances.LogsDebugLabel;
    public string LogsWarningsLabel => Instances.LogsWarningsLabel;
    public string LogsErrorsLabel => Instances.LogsErrorsLabel;
    public string LogsTracingLabel => Instances.LogsTracingLabel;
    public string LogsShowInFolderLabel => Instances.LogsShowInFolderLabel;
    public string LogsLevelSummary => Instances.LogsLevelSummary;
    public bool HasAdditionalLogsLevels => Instances.HasAdditionalLogsLevels;
    public string LogsAdditionalLevelCountText => Instances.LogsAdditionalLevelCountText;
    public bool CanShowLogsInFolder => Instances.CanShowLogsInFolder;
    public bool HasModSelection => Instances.HasModSelection;
    public bool HasModUpdates => Instances.HasModUpdates;
    public bool HasLogs => Instances.HasLogs;
    public bool IsLogsEmpty => Instances.IsLogsEmpty;
    public bool CanLoadMoreModCatalog => Instances.CanLoadMoreModCatalog;
    public bool HasModCatalogPreview => Instances.HasModCatalogPreview;
    public bool IsModCatalogPreviewMounted => Instances.IsModCatalogPreviewMounted;
    public bool HasModCatalogPreviewImage => Instances.HasModCatalogPreviewImage;
    public bool HasModCatalogPreviewFiles => Instances.HasModCatalogPreviewFiles;
    public bool HasMultipleModCatalogPreviewScreenshots => Instances.HasMultipleModCatalogPreviewScreenshots;
    public bool CanShowPreviousModCatalogScreenshot => Instances.CanShowPreviousModCatalogScreenshot;
    public bool CanShowNextModCatalogScreenshot => Instances.CanShowNextModCatalogScreenshot;
    public bool CanInstallModCatalogPreview => Instances.CanInstallModCatalogPreview;
    public int SelectedCatalogModCount => Instances.SelectedCatalogModCount;
    public bool HasSelectedCatalogMods => Instances.HasSelectedCatalogMods;
    public bool CanInstallSelectedCatalogMods => Instances.CanInstallSelectedCatalogMods;
    public bool CanOpenModCatalogInstallConfirmation => Instances.CanOpenModCatalogInstallConfirmation;
    public bool HasModCatalogInstallConfirmation => Instances.HasModCatalogInstallConfirmation;
    public double ModCatalogInstallProgress => Instances.ModCatalogInstallProgress;
    public int ModCatalogInstallCompletedCount => Instances.ModCatalogInstallCompletedCount;
    public string ModCatalogInstallProgressText => Instances.ModCatalogInstallProgressText;
    public string SelectedModCountText => Instances.SelectedModCountText;
    public string ModUpdateCountText => Instances.ModUpdateCountText;
    public string InstanceModsUpdatesAvailableText => Instances.InstanceModsUpdatesAvailableText;
    public string InstanceModsFooterText => Instances.InstanceModsFooterText;
    public string InstanceNotInstalledTitle => Instances.InstanceNotInstalledTitle;
    public string InstanceNotInstalledHint => Instances.InstanceNotInstalledHint;
    public string RefreshLabel => Instances.RefreshLabel;
    public string InstallLabel => Instances.InstallLabel;
    public string InstalledLabel => Instances.InstalledLabel;
    public string EnabledLabel => Instances.EnabledLabel;
    public string DisabledLabel => Instances.DisabledLabel;
    public string InstanceContentBackLabel => Instances.InstanceContentBackLabel;
    public string ManagedInstancePlayLabel => Instances.ManagedInstancePlayLabel;
    public string ManagedInstanceInstallLabel => Instances.ManagedInstanceInstallLabel;
    public string ManagedInstanceOpenFolderLabel => Instances.ManagedInstanceOpenFolderLabel;
    public string ManagedInstanceDeleteLabel => Instances.ManagedInstanceDeleteLabel;
    public string ManagedInstanceDeleteTitle => Instances.ManagedInstanceDeleteTitle;
    public string ManagedInstanceDeleteHint => Instances.ManagedInstanceDeleteHint;
    public string ManagedInstanceActionLabel => Instances.ManagedInstanceActionLabel;
    public string ManagedInstanceActionCancelLabel => Instances.ManagedInstanceActionCancelLabel;
    public string ManagedInstanceActionStatusText => Instances.ManagedInstanceActionStatusText;
    public string ManagedInstanceActionMetricText => Instances.ManagedInstanceActionMetricText;
    public string InstanceStatusInfoLabel => Instances.InstanceStatusInfoLabel;
    public string InstancePlayTimeInfoLabel => Instances.InstancePlayTimeInfoLabel;
    public string InstanceModsInfoLabel => Instances.InstanceModsInfoLabel;
    public string InstanceWorldsInfoLabel => Instances.InstanceWorldsInfoLabel;
    public string InstanceModsCountText => Instances.InstanceModsCountText;
    public string InstanceWorldsCountText => Instances.InstanceWorldsCountText;
    public string InstanceSectionTitle => Instances.InstanceSectionTitle;
    public bool HasInstances => Instances.HasInstances;
    public bool HasSelectedInstance => Instances.HasSelectedInstance;
    public bool HasManagedInstance => Instances.HasManagedInstance;
    public bool HasAvailableInstanceVersions => Instances.HasAvailableInstanceVersions;
    public bool HasInstanceCreationError => Instances.HasInstanceCreationError;
    public bool HasInstanceContentError => Instances.HasInstanceContentError;
    public bool IsSelectedInstanceInstalled => Instances.IsSelectedInstanceInstalled;
    public bool IsManagedInstanceInstalled => Instances.IsManagedInstanceInstalled;
    public bool IsManagedInstanceActionActive => Instances.IsManagedInstanceActionActive;
    public bool IsManagedInstanceActionRunning => Instances.IsManagedInstanceActionRunning;
    public bool ShouldSpinManagedInstanceAction => Instances.ShouldSpinManagedInstanceAction;
    public bool IsManagedInstanceCancellationArmed => Instances.IsManagedInstanceCancellationArmed;
    public bool CanRunManagedInstanceAction => Instances.CanRunManagedInstanceAction;
    public bool CanOpenManagedInstanceFolder => Instances.CanOpenManagedInstanceFolder;
    public bool CanDeleteManagedInstance => Instances.CanDeleteManagedInstance;
    public bool IsInstanceOverviewSection => Instances.IsInstanceOverviewSection;
    public bool IsInstanceModsSection => Instances.IsInstanceModsSection;
    public bool IsInstanceBrowseSection => Instances.IsInstanceBrowseSection;
    public bool IsInstanceWorldsSection => Instances.IsInstanceWorldsSection;
    public bool IsInstanceLogsSection => Instances.IsInstanceLogsSection;
    public bool HasInstalledMods => Instances.HasInstalledMods;
    public bool HasModCatalogItems => Instances.HasModCatalogItems;
    public bool HasInstanceWorlds => Instances.HasInstanceWorlds;
    public bool IsInstalledModsEmpty => Instances.IsInstalledModsEmpty;
    public bool IsModCatalogEmpty => Instances.IsModCatalogEmpty;
    public bool IsInstanceWorldsEmpty => Instances.IsInstanceWorldsEmpty;
    public bool CanCreateInstance => Instances.CanCreateInstance;
    public bool IsCreateReleaseBranch => Instances.IsCreateReleaseBranch;
    public bool IsCreatePreReleaseBranch => Instances.IsCreatePreReleaseBranch;
    public ObservableCollection<InstanceItemViewModel> AllInstances => Instances.AllInstances;
    public ObservableCollection<InstanceVersionItemViewModel> AvailableInstanceVersions => Instances.AvailableInstanceVersions;
    public ObservableCollection<InstanceModItemViewModel> InstalledMods => Instances.InstalledMods;
    public ObservableCollection<InstanceModItemViewModel> VisibleInstalledMods => Instances.VisibleInstalledMods;
    public ObservableCollection<ModCatalogItemViewModel> ModCatalogItems => Instances.ModCatalogItems;
    public ObservableCollection<ModCatalogFileItemViewModel> ModCatalogPreviewFiles => Instances.ModCatalogPreviewFiles;
    public ObservableCollection<ModCatalogInstallItemViewModel> ModCatalogInstallItems => Instances.ModCatalogInstallItems;
    public ObservableCollection<InstanceWorldItemViewModel> InstanceWorlds => Instances.InstanceWorlds;
    public ObservableCollection<InstanceLogLineViewModel> LogsLines => Instances.LogsLines;
    public ObservableCollection<InstanceListOptionViewModel> ModCatalogCategories => Instances.ModCatalogCategories;
    public ObservableCollection<InstanceListOptionViewModel> ModCatalogSortOptions => Instances.ModCatalogSortOptions;

    public IRelayCommand OpenInstanceCreatorCommand => Instances.OpenInstanceCreatorCommand;
    public IRelayCommand CloseInstanceCreatorCommand => Instances.CloseInstanceCreatorCommand;
    public IRelayCommand SetNewInstanceBranchCommand => Instances.SetNewInstanceBranchCommand;
    public IRelayCommand SelectNewInstanceVersionCommand => Instances.SelectNewInstanceVersionCommand;
    public IRelayCommand CreateInstanceCommand => Instances.CreateInstanceCommand;
    public IRelayCommand OpenInstanceDetailsCommand => Instances.OpenInstanceDetailsCommand;
    public IRelayCommand SelectInstanceSectionCommand => Instances.SelectInstanceSectionCommand;
    public IRelayCommand CloseInstanceSectionCommand => Instances.CloseInstanceSectionCommand;
    public IAsyncRelayCommand RefreshInstanceModsCommand => Instances.RefreshInstanceModsCommand;
    public IAsyncRelayCommand SearchModCatalogCommand => Instances.SearchModCatalogCommand;
    public IAsyncRelayCommand LoadMoreModCatalogCommand => Instances.LoadMoreModCatalogCommand;
    public IRelayCommand ToggleModCatalogSelectionCommand => Instances.ToggleModCatalogSelectionCommand;
    public IRelayCommand ClearModCatalogSelectionCommand => Instances.ClearModCatalogSelectionCommand;
    public IRelayCommand OpenModCatalogInstallConfirmationCommand => Instances.OpenModCatalogInstallConfirmationCommand;
    public IRelayCommand CloseModCatalogInstallConfirmationCommand => Instances.CloseModCatalogInstallConfirmationCommand;
    public IAsyncRelayCommand InstallSelectedCatalogModsCommand => Instances.InstallSelectedCatalogModsCommand;
    public IAsyncRelayCommand SelectModCatalogPreviewCommand => Instances.SelectModCatalogPreviewCommand;
    public IRelayCommand CloseModCatalogPreviewCommand => Instances.CloseModCatalogPreviewCommand;
    public IRelayCommand SelectModCatalogPreviewFileCommand => Instances.SelectModCatalogPreviewFileCommand;
    public IAsyncRelayCommand ShowPreviousModCatalogScreenshotCommand => Instances.ShowPreviousModCatalogScreenshotCommand;
    public IAsyncRelayCommand ShowNextModCatalogScreenshotCommand => Instances.ShowNextModCatalogScreenshotCommand;
    public IAsyncRelayCommand InstallModCatalogPreviewCommand => Instances.InstallModCatalogPreviewCommand;
    public IAsyncRelayCommand RefreshInstanceWorldsCommand => Instances.RefreshInstanceWorldsCommand;
    public IAsyncRelayCommand ToggleModCommand => Instances.ToggleModCommand;
    public IAsyncRelayCommand DeleteModCommand => Instances.DeleteModCommand;
    public IRelayCommand SelectAllInstalledModsCommand => Instances.SelectAllInstalledModsCommand;
    public IRelayCommand ClearInstalledModsSelectionCommand => Instances.ClearInstalledModsSelectionCommand;
    public IAsyncRelayCommand EnableSelectedModsCommand => Instances.EnableSelectedModsCommand;
    public IAsyncRelayCommand DisableSelectedModsCommand => Instances.DisableSelectedModsCommand;
    public IAsyncRelayCommand DeleteSelectedModsCommand => Instances.DeleteSelectedModsCommand;
    public IAsyncRelayCommand CheckModUpdatesCommand => Instances.CheckModUpdatesCommand;
    public IAsyncRelayCommand UpdateAllModsWithUpdatesCommand => Instances.UpdateAllModsWithUpdatesCommand;
    public IAsyncRelayCommand ImportModsCommand => Instances.ImportModsCommand;
    public IAsyncRelayCommand OpenInstalledModPageCommand => Instances.OpenInstalledModPageCommand;
    public IAsyncRelayCommand OpenCatalogModPageCommand => Instances.OpenCatalogModPageCommand;
    public IRelayCommand ClearLogsCommand => Instances.ClearLogsCommand;
    public IAsyncRelayCommand ShowLogsInFolderCommand => Instances.ShowLogsInFolderCommand;
    public IAsyncRelayCommand RunManagedInstanceCommand => Instances.RunManagedInstanceCommand;
    public IAsyncRelayCommand OpenManagedInstanceFolderCommand => Instances.OpenManagedInstanceFolderCommand;
    public IRelayCommand DeleteManagedInstanceCommand => Instances.DeleteManagedInstanceCommand;
    public IRelayCommand CancelActivityCommand => Instances.CancelActivityCommand;

    public void MoveInstance(string instanceId, int targetIndex)
        => Instances.MoveInstance(instanceId, targetIndex);
    public ObservableCollection<NewsItemViewModel> LatestNews => News.LatestNews;
    public bool HasLoadedNews => News.HasLoadedNews;
    public string NewsLoadingLabel => News.NewsLoadingLabel;
    public string NewsEmptyLabel => News.NewsEmptyLabel;
    public string BackLabel => News.BackLabel;
    public string OpenOriginalLabel => News.OpenOriginalLabel;
    public string ArticleLoadingLabel => News.ArticleLoadingLabel;
    public string SelectArticleLabel => News.SelectArticleLabel;
    public string LoadMoreLabel => News.LoadMoreLabel;
    public NewsItemViewModel? FeaturedNews
    {
        get => News.FeaturedNews;
        set => News.FeaturedNews = value;
    }
    public NewsItemViewModel? SelectedNewsItem
    {
        get => News.SelectedNewsItem;
        set => News.SelectedNewsItem = value;
    }
    public NewsArticleViewModel? SelectedNewsArticle
    {
        get => News.SelectedNewsArticle;
        set => News.SelectedNewsArticle = value;
    }
    public bool IsNewsArticleBodyVisible
    {
        get => News.IsNewsArticleBodyVisible;
        set => News.IsNewsArticleBodyVisible = value;
    }
    public bool IsNewsArticleBodySkeletonVisible
    {
        get => News.IsNewsArticleBodySkeletonVisible;
        set => News.IsNewsArticleBodySkeletonVisible = value;
    }
    public bool IsNewsArticleBodySkeletonFadingOut
    {
        get => News.IsNewsArticleBodySkeletonFadingOut;
        set => News.IsNewsArticleBodySkeletonFadingOut = value;
    }
    public bool IsNewsArticleLoading
    {
        get => News.IsNewsArticleLoading;
        set => News.IsNewsArticleLoading = value;
    }
    public bool IsNewsArticleSkeletonVisible
    {
        get => News.IsNewsArticleSkeletonVisible;
        set => News.IsNewsArticleSkeletonVisible = value;
    }
    public string NewsArticleError
    {
        get => News.NewsArticleError;
        set => News.NewsArticleError = value;
    }
    public bool IsNewsLoading
    {
        get => News.IsNewsLoading;
        set => News.IsNewsLoading = value;
    }
    public string NewsError
    {
        get => News.NewsError;
        set => News.NewsError = value;
    }
    public bool IsWideNewsLayout
    {
        get => News.IsWideNewsLayout;
        set => News.IsWideNewsLayout = value;
    }
    public bool IsCompactNewsTransitionActive
    {
        get => News.IsCompactNewsTransitionActive;
        set => News.IsCompactNewsTransitionActive = value;
    }
    public bool IsNewsArticleScrolled
    {
        get => News.IsNewsArticleScrolled;
        set => News.IsNewsArticleScrolled = value;
    }
    public bool IsLoadingMoreNews
    {
        get => News.IsLoadingMoreNews;
        set => News.IsLoadingMoreNews = value;
    }
    public int CompactNewsPageIndex
    {
        get => News.CompactNewsPageIndex;
        set => News.CompactNewsPageIndex = value;
    }

    public bool HasFeaturedNews => News.HasFeaturedNews;
    public bool HasNewsError => News.HasNewsError;
    public bool IsNewsReady => News.IsNewsReady;
    public bool IsNewsEmpty => News.IsNewsEmpty;
    public bool HasSelectedNewsItem => News.HasSelectedNewsItem;
    public bool IsNewsFeedVisible => News.IsNewsFeedVisible;
    public bool IsNewsArticleContext => News.IsNewsArticleContext;
    public bool IsNewsArticleEmpty => News.IsNewsArticleEmpty;
    public bool HasNewsArticleError => News.HasNewsArticleError;
    public bool IsNewsLandingVisible => IsNews && News.IsNewsLandingVisible;
    public bool IsNewsArticleVisible => IsNews && News.IsNewsArticleVisible;
    public string NewsArticleDisplayTitle => News.NewsArticleDisplayTitle;
    public string NewsArticleDisplayMetadata => News.NewsArticleDisplayMetadata;
    public bool IsNewsArticleBodyPreparing => News.IsNewsArticleBodyPreparing;
    public bool IsNewsArticleStatusVisible => IsNews && News.IsNewsArticleStatusVisible;
    public bool IsCompactNewsLayout => News.IsCompactNewsLayout;
    public bool HasMoreNews => News.HasMoreNews;
    public bool CanShowLoadMore => News.CanShowLoadMore;

    public IAsyncRelayCommand LoadMoreNewsCommand => News.LoadMoreNewsCommand;
    public IAsyncRelayCommand CloseNewsArticleCommand => News.CloseNewsArticleCommand;

    public Task ImportModFilesAsync(IReadOnlyList<string>? filePaths)
        => Instances.ImportModFilesAsync(filePaths);

    public void ArmManagedInstanceCancellation()
        => Instances.ArmManagedInstanceCancellation();

}
