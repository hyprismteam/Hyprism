// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstancesView : UserControl
{
    public const double ModCatalogContentMaxWidth = 820;

    private static readonly TimeSpan CompactContentTransitionDuration = MotionDurations.CompactPageSlide;
    private static readonly TimeSpan CompactSectionSlideDuration = MotionDurations.CompactSectionSlide;
    private static readonly TimeSpan WideSectionSlideDuration = MotionDurations.ContentFade;
    private static readonly TimeSpan VersionLoadingFadeDuration = MotionDurations.VersionLoadingFade;
    private static readonly TimeSpan ModCatalogSearchFadeDuration = MotionDurations.ContentFade;
    private readonly WizardHost _creatorWizard;
    private readonly WizardScreenTransition _modCatalogInstallTransition;
    private readonly AdaptiveMasterDetailHost _layoutHost;
    private readonly ReorderableListController _instanceReorder;
    private INotifyPropertyChanged? _viewModel;
    private bool _creatorOpenedFromCompactList;
    private bool _creatorTransitionActive;
    private int _creatorNavigationRevision;
    private CancellationTokenSource? _sectionAnimationCancellation;
    private CancellationTokenSource? _versionLoadingCancellation;
    private CancellationTokenSource? _modCatalogLoadingCancellation;
    private bool _modDropActive;

    public InstancesView()
    {
        InitializeComponent();
        _modCatalogInstallTransition = new WizardScreenTransition(
            ModCatalogBrowseContent,
            ModCatalogInstallScreen);
        _creatorWizard = new WizardHost(
            InstancesOverview,
            InstanceCreatorScreen,
            InstancesListPane,
            InstanceWizardReveal.Anchor,
            InstanceWizardReveal.MotionTarget,
            InstanceWizardReveal.Animation);
        _layoutHost = new AdaptiveMasterDetailHost(
            InstancesLayout,
            InstancesListPane,
            InstancesContent,
            CompactInstanceToolbar,
            InstanceHubContent,
            compact =>
            {
                Classes.Set("compact", compact);
                Classes.Set("wide", !compact);
            });
        _instanceReorder = new ReorderableListController(
            InstancesItems,
            InstancesLayout,
            InstanceDragPreview,
            InstancesListPane);
        DragDrop.SetAllowDrop(ModsDropZone, true);
        ModsDropZone.AddHandler(DragDrop.DragEnterEvent, OnModFilesDragEntered);
        ModsDropZone.AddHandler(DragDrop.DragLeaveEvent, OnModFilesDragLeft);
        ModsDropZone.AddHandler(DragDrop.DropEvent, OnModFilesDropped);
        AddHandler(KeyDownEvent, OnInstancesKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += (_, args) => UpdateLayout(args.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateLayout(Bounds.Width);
        UpdateBranchIndicator(animate: false);
        ApplyVersionLoadingStateImmediately();
        ApplyModCatalogLoadingStateImmediately();
        ApplyModCatalogInstallStateImmediately();
        ApplySectionStateImmediately();
        ApplyModCatalogModalBackground();

        if (DataContext is InstancesViewModel { IsInstanceCreatorOpen: true })
            _ = PlayCreatorOpenAnimationAsync();
        else
            HideCreatorImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstancesViewModel.HasInstances))
            UpdateLayout(Bounds.Width);

        if (args.PropertyName is nameof(InstancesViewModel.NewInstanceBranch) &&
            DataContext is InstancesViewModel { IsInstanceCreatorOpen: true })
            UpdateBranchIndicator(animate: true);

        if (args.PropertyName is nameof(InstancesViewModel.InstanceSection))
        {
            if (DataContext is InstancesViewModel { IsInstanceOverviewSection: true })
                _ = PlaySectionCloseAnimationAsync();
            else
                _ = PlaySectionOpenAnimationAsync();
        }

        if (args.PropertyName is nameof(InstancesViewModel.IsInstanceVersionsLoading))
        {
            if (DataContext is InstancesViewModel { IsInstanceVersionsLoading: true })
                ShowVersionLoading();
            else
                _ = HideVersionLoadingAsync();
        }

        if (args.PropertyName is nameof(InstancesViewModel.IsModCatalogLoading))
        {
            if (DataContext is InstancesViewModel { IsModCatalogLoading: true })
                ShowModCatalogLoading();
            else
                _ = HideModCatalogLoadingAsync();
        }

        if (args.PropertyName is nameof(InstancesViewModel.IsInstallingSelectedCatalogMods))
        {
            if (DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: true })
                _ = PlayModCatalogInstallOpenAnimationAsync();
            else
                _ = PlayModCatalogInstallCloseAnimationAsync();
        }

        if (args.PropertyName is nameof(InstancesViewModel.LogsRevision))
            Dispatcher.UIThread.Post(ScrollLogsToBottom, DispatcherPriority.Loaded);

        if (args.PropertyName is nameof(InstancesViewModel.IsInstanceCreatorOpen))
        {
            if (DataContext is InstancesViewModel { IsInstanceCreatorOpen: true })
                _ = PlayCreatorOpenAnimationAsync();
            else
                _ = PlayCreatorCloseAnimationAsync();
        }

        if (args.PropertyName is nameof(InstancesViewModel.HasModCatalogPreview) or
            nameof(InstancesViewModel.HasModCatalogInstallConfirmation))
            ApplyModCatalogModalBackground();
    }

    private void ApplyModCatalogModalBackground()
    {
        var isOpen = DataContext is InstancesViewModel viewModel &&
            (viewModel.HasModCatalogPreview || viewModel.HasModCatalogInstallConfirmation);
        InstancesLayout.IsHitTestVisible = !isOpen;
        ((BlurEffect)InstancesLayout.Effect!).Radius = isOpen ? 6 : 0;
    }

    private void OnModCatalogModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.CompleteModCatalogPreviewClose();
        ApplyModCatalogModalBackground();
    }

    private void OnModCatalogInstallModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.CompleteModCatalogInstallConfirmationClose();
        ApplyModCatalogModalBackground();
    }

    private void OnInstancesKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not Key.Escape ||
            (!TryCloseModCatalogPreview() && !TryCloseModCatalogInstallConfirmation()))
            return;

        args.Handled = true;
    }

    public bool TryCloseModCatalogPreview()
    {
        if (DataContext is not InstancesViewModel { HasModCatalogPreview: true } viewModel)
            return false;

        viewModel.CloseModCatalogPreviewCommand.Execute(null);
        return true;
    }

    public bool TryCloseModCatalogInstallConfirmation()
    {
        if (DataContext is not InstancesViewModel { HasModCatalogInstallConfirmation: true } viewModel)
            return false;

        viewModel.CloseModCatalogInstallConfirmationCommand.Execute(null);
        return true;
    }

    private void UpdateLayout(double width)
    {
        if (width <= 0 || DataContext is not InstancesViewModel viewModel)
            return;

        var hasInstances = viewModel.HasInstances;
        var wasCompact = _layoutHost.IsCompact;
        _layoutHost.Update(width, hasInstances);
        UpdateInstanceSectionContentWidth();
        var layoutModeChanged = wasCompact != _layoutHost.IsCompact;

        if (!hasInstances)
        {
            EnsureSingleLayoutRow();
            if (!_creatorTransitionActive)
                _creatorWizard.ResetNavigationPane();
            InstancesLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(InstancesContent, 0);
            Grid.SetRowSpan(InstancesContent, 1);
            if (layoutModeChanged)
                ApplySectionStateImmediately();
            return;
        }

        EnsureSingleLayoutRow();
        InstancesLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        Grid.SetRow(InstancesListPane, 0);
        Grid.SetRow(InstancesContent, 0);
        Grid.SetRowSpan(InstancesContent, 1);

        if (!_layoutHost.IsCompact)
        {
            if (_creatorTransitionActive)
            {
                if (layoutModeChanged)
                    ApplySectionStateImmediately();
                return;
            }

            if (viewModel.IsInstanceCreatorOpen)
                _creatorWizard.HideNavigationPane(animate: false);
            else
                _creatorWizard.ShowNavigationPane(animate: false);
            if (layoutModeChanged)
                ApplySectionStateImmediately();
            return;
        }

        if (!_creatorTransitionActive)
            _creatorWizard.ResetNavigationPane();
        Grid.SetRowSpan(InstancesContent, 1);
        if (layoutModeChanged)
            ApplySectionStateImmediately();
    }

    private void EnsureSingleLayoutRow()
    {
        while (InstancesLayout.RowDefinitions.Count > 1)
            InstancesLayout.RowDefinitions.RemoveAt(InstancesLayout.RowDefinitions.Count - 1);
    }

    private void UpdateInstanceSectionContentWidth()
    {
        var maxWidth = _layoutHost.IsCompact
            ? double.PositiveInfinity
            : AdaptiveMasterDetailHost.DefaultContentMaxWidth;
        InstalledModsSection.MaxWidth = maxWidth;
        ModCatalogSection.MaxWidth = _layoutHost.IsCompact
            ? double.PositiveInfinity
            : ModCatalogContentMaxWidth;
        InstanceLogsSection.MaxWidth = ModCatalogSection.MaxWidth;
    }

    private void OnInstanceClicked(object? sender, RoutedEventArgs args)
    {
        _layoutHost.RememberDetail();
        if (!_layoutHost.IsCompact)
            return;

        _layoutHost.OpenDetail();
    }

    private void OnManagedInstanceActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.ArmManagedInstanceCancellation();
    }

    private void OnInstanceDragHandlePressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Border { DataContext: InstanceItemViewModel instance } handle)
            return;

        _instanceReorder.Begin(handle, instance.Id, args, 66, () =>
        {
            InstanceDragPreviewName.Text = instance.Name;
            InstanceDragPreviewBranch.Text = instance.Branch;
        });
    }

    private void OnInstanceDragHandleMoved(object? sender, PointerEventArgs args)
        => _instanceReorder.Move(args);

    private void OnInstanceDragHandleReleased(object? sender, PointerReleasedEventArgs args)
    {
        _instanceReorder.Complete(
            args,
            (instanceId, targetIndex) =>
            {
                if (DataContext is InstancesViewModel viewModel)
                    viewModel.MoveInstance(instanceId, targetIndex);
            },
            () =>
            {
                InstanceDragPreviewName.Text = string.Empty;
                InstanceDragPreviewBranch.Text = string.Empty;
            });
    }

    private void OnOpenCreatorClicked(object? sender, RoutedEventArgs args)
    {
        _creatorOpenedFromCompactList = _layoutHost.IsOpeningWizardFromMaster();
        if (_layoutHost.IsCompact && !_creatorOpenedFromCompactList)
            _layoutHost.OpenDetail();

        if (DataContext is InstancesViewModel viewModel)
            viewModel.OpenInstanceCreatorCommand.Execute(null);
    }

    private void OnCloseDeleteInstanceFlyoutClicked(object? sender, RoutedEventArgs args)
    {
        DeleteInstanceButton.Flyout?.Hide();
        CompactDeleteInstanceButton.Flyout?.Hide();
        CompactInstanceMenuPopup.IsRequestedOpen = false;
    }

    private void OnCloseCompactInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => CompactInstanceMenuPopup.IsRequestedOpen = false;

    private void OnToggleCompactInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => CompactInstanceMenuPopup.IsRequestedOpen = !CompactInstanceMenuPopup.IsRequestedOpen;

    private void OnCompactInstanceBackClicked(object? sender, RoutedEventArgs args)
        => TryCloseCompactContent();

    public bool TryCloseCompactContent()
    {
        if (!_layoutHost.IsCompact || !_layoutHost.IsDetailOpen)
            return false;

        if (DataContext is InstancesViewModel { IsInstanceCreatorOpen: true } viewModel)
        {
            viewModel.CloseInstanceCreatorCommand.Execute(null);
            return true;
        }

        return _layoutHost.TryCloseDetail();
    }

    public bool TryNavigateBack()
    {
        if (TryCloseModCatalogPreview() || TryCloseModCatalogInstallConfirmation())
            return true;

        if (DataContext is InstancesViewModel { IsInstanceOverviewSection: false } viewModel)
        {
            viewModel.CloseInstanceSectionCommand.Execute(null);
            return true;
        }

        return TryCloseCompactContent();
    }

    private async Task PlayCreatorOpenAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        _creatorTransitionActive = true;
        try
        {
            if (_creatorOpenedFromCompactList && _layoutHost.IsCompact)
            {
                await _creatorWizard.ShowWizardForCompactEntryAsync();
                if (revision != _creatorNavigationRevision ||
                    DataContext is not InstancesViewModel { IsInstanceCreatorOpen: true })
                {
                    return;
                }

                _layoutHost.OpenDetail();
                UpdateBranchIndicator(animate: false);
                await Task.Delay(CompactContentTransitionDuration);
                return;
            }

            if (!_layoutHost.IsCompact &&
                DataContext is InstancesViewModel { HasInstances: true })
            {
                _creatorWizard.HideNavigationPane(animate: true);
            }

            await _creatorWizard.OpenAsync(
                () => DataContext is InstancesViewModel { IsInstanceCreatorOpen: true },
                () => UpdateBranchIndicator(animate: false));
        }
        finally
        {
            if (revision == _creatorNavigationRevision)
            {
                _creatorTransitionActive = false;
                UpdateLayout(Bounds.Width);
            }
        }
    }

    private async Task PlayCreatorCloseAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        _creatorTransitionActive = true;
        try
        {
            if (_creatorOpenedFromCompactList && _layoutHost.IsCompact)
            {
                _creatorWizard.Cancel();
                _layoutHost.TryCloseDetail();
                await Task.Delay(CompactContentTransitionDuration);
                if (revision == _creatorNavigationRevision &&
                    DataContext is InstancesViewModel { IsInstanceCreatorOpen: false })
                {
                    _creatorWizard.ShowOverviewImmediately();
                    _creatorOpenedFromCompactList = false;
                }

                return;
            }

            await _creatorWizard.CloseAsync(
                () => DataContext is InstancesViewModel { IsInstanceCreatorOpen: false },
                () =>
                {
                    if (!_layoutHost.IsCompact &&
                        DataContext is InstancesViewModel { HasInstances: true })
                    {
                        _creatorWizard.ShowNavigationPane(animate: true);
                    }

                    _creatorOpenedFromCompactList = false;
                });
        }
        finally
        {
            if (revision == _creatorNavigationRevision)
            {
                _creatorTransitionActive = false;
                if (_layoutHost.IsCompact)
                    UpdateLayout(Bounds.Width);
            }
        }
    }

    private void HideCreatorImmediately()
    {
        ++_creatorNavigationRevision;
        _creatorTransitionActive = false;
        _creatorOpenedFromCompactList = false;
        _creatorWizard.ShowOverviewImmediately();
        if (!_layoutHost.IsCompact &&
            DataContext is InstancesViewModel { HasInstances: true })
        {
            _creatorWizard.ShowNavigationPane(animate: false);
        }
    }

    private void ApplyModCatalogInstallStateImmediately()
    {
        if (DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: true })
            _modCatalogInstallTransition.ShowWizardImmediately();
        else
            _modCatalogInstallTransition.ShowOverviewImmediately();
    }

    private Task PlayModCatalogInstallOpenAnimationAsync()
        => _modCatalogInstallTransition.OpenAsync(
            () => DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: true });

    private Task PlayModCatalogInstallCloseAnimationAsync()
        => _modCatalogInstallTransition.CloseAsync(
            () => DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: false },
            () =>
            {
                if (DataContext is InstancesViewModel viewModel)
                    viewModel.CompleteModCatalogInstallation();
            });

    private async Task PlaySectionOpenAnimationAsync()
    {
        CancelSectionAnimation();
        if (_layoutHost.IsCompact)
        {
            await PlayCompactSectionOpenAnimationAsync();
            return;
        }

        if (!InstanceHubScreen.IsVisible)
        {
            ApplySectionStateImmediately();
            return;
        }

        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var hubTranslation = (TranslateTransform)InstanceHubScreen.RenderTransform!;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(WideSectionSlideDuration);
        InstanceHubScreen.IsHitTestVisible = false;
        InstanceSectionScreen.IsHitTestVisible = false;
        InstanceHubScreen.Opacity = 0;
        hubTranslation.X = -28;

        try
        {
            await Task.Delay(WizardScreenTransition.PhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not InstancesViewModel { IsInstanceOverviewSection: false })
            {
                return;
            }

            InstanceHubScreen.IsVisible = false;
            PrepareSectionForEntry(sectionTranslation);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested)
                return;

            InstanceSectionScreen.IsHitTestVisible = true;
            InstanceSectionScreen.Opacity = 1;
            sectionTranslation.X = 0;
        }
        catch (OperationCanceledException)
        {
            // A reverse section navigation replaces the pending transition
        }
    }

    private async Task PlaySectionCloseAnimationAsync()
    {
        CancelSectionAnimation();
        if (_layoutHost.IsCompact)
        {
            await PlayCompactSectionCloseAnimationAsync();
            return;
        }

        if (!InstanceSectionScreen.IsVisible)
        {
            ApplySectionStateImmediately();
            return;
        }

        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(WideSectionSlideDuration);
        InstanceSectionScreen.IsHitTestVisible = false;
        InstanceSectionScreen.Opacity = 0;
        sectionTranslation.X = 28;

        try
        {
            await Task.Delay(WizardScreenTransition.PhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not InstancesViewModel { IsInstanceOverviewSection: true })
            {
                return;
            }

            InstanceSectionScreen.IsVisible = false;
            if (DataContext is InstancesViewModel viewModel)
                viewModel.CompleteInstanceSectionClose();
            var hubTranslation = (TranslateTransform)InstanceHubScreen.RenderTransform!;
            PrepareHubForEntry(hubTranslation);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested)
                return;

            InstanceHubScreen.IsHitTestVisible = true;
            InstanceHubScreen.Opacity = 1;
            hubTranslation.X = 0;
        }
        catch (OperationCanceledException)
        {
            // Opening another section replaces the pending transition
        }
    }

    private void ApplySectionStateImmediately()
    {
        CancelSectionAnimation();
        var showHub = DataContext is not InstancesViewModel { IsInstanceOverviewSection: false };
        var hubTranslation = (TranslateTransform)InstanceHubScreen.RenderTransform!;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        var hubTransitions = InstanceHubScreen.Transitions;
        var sectionTransitions = InstanceSectionScreen.Transitions;
        var hubTranslationTransitions = hubTranslation.Transitions;
        var sectionTranslationTransitions = sectionTranslation.Transitions;
        if (showHub && DataContext is InstancesViewModel viewModel)
            viewModel.CompleteInstanceSectionClose();
        else if (!showHub && DataContext is InstancesViewModel sectionViewModel)
            sectionViewModel.SynchronizeDisplayedInstanceSection();
        InstanceHubScreen.Transitions = null;
        InstanceSectionScreen.Transitions = null;
        hubTranslation.Transitions = null;
        sectionTranslation.Transitions = null;
        var compact = _layoutHost.IsCompact;
        InstanceHubScreen.IsVisible = compact || showHub;
        InstanceHubScreen.IsHitTestVisible = showHub;
        InstanceHubScreen.Opacity = compact || showHub ? 1 : 0;
        hubTranslation.X = compact || showHub ? 0 : -28;
        InstanceSectionScreen.IsVisible = !showHub;
        InstanceSectionScreen.IsHitTestVisible = !showHub;
        InstanceSectionScreen.Opacity = showHub ? 0 : 1;
        sectionTranslation.X = showHub
            ? compact ? GetSectionSlideDistance() : 28
            : 0;
        InstanceHubScreen.Transitions = hubTransitions;
        InstanceSectionScreen.Transitions = sectionTransitions;
        hubTranslation.Transitions = hubTranslationTransitions;
        sectionTranslation.Transitions = sectionTranslationTransitions;
        SetSectionTranslationDuration(compact ? CompactSectionSlideDuration : WideSectionSlideDuration);
    }

    private async Task PlayCompactSectionOpenAnimationAsync()
    {
        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(CompactSectionSlideDuration);

        InstanceHubScreen.IsVisible = true;
        InstanceHubScreen.IsHitTestVisible = false;
        InstanceHubScreen.Opacity = 1;
        ((TranslateTransform)InstanceHubScreen.RenderTransform!).X = 0;
        PrepareCompactSectionForEntry(sectionTranslation);

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (cancellationToken.IsCancellationRequested ||
            DataContext is not InstancesViewModel { IsInstanceOverviewSection: false })
        {
            return;
        }

        InstanceSectionScreen.IsHitTestVisible = true;
        sectionTranslation.X = 0;
    }

    private async Task PlayCompactSectionCloseAnimationAsync()
    {
        if (!InstanceSectionScreen.IsVisible)
        {
            ApplySectionStateImmediately();
            return;
        }

        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(CompactSectionSlideDuration);
        InstanceSectionScreen.IsHitTestVisible = false;
        InstanceHubScreen.IsVisible = true;
        InstanceHubScreen.IsHitTestVisible = false;
        InstanceHubScreen.Opacity = 1;
        ((TranslateTransform)InstanceHubScreen.RenderTransform!).X = 0;
        sectionTranslation.X = GetSectionSlideDistance();

        try
        {
            await Task.Delay(CompactSectionSlideDuration + TimeSpan.FromMilliseconds(20), cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not InstancesViewModel { IsInstanceOverviewSection: true })
            {
                return;
            }

            InstanceSectionScreen.IsVisible = false;
            InstanceSectionScreen.Opacity = 0;
            if (DataContext is InstancesViewModel viewModel)
                viewModel.CompleteInstanceSectionClose();
            InstanceHubScreen.IsHitTestVisible = true;
        }
        catch (OperationCanceledException)
        {
            // Opening a section again replaces the pending compact slide
        }
    }

    private void PrepareCompactSectionForEntry(TranslateTransform translation)
    {
        var transitions = InstanceSectionScreen.Transitions;
        var translationTransitions = translation.Transitions;
        InstanceSectionScreen.Transitions = null;
        translation.Transitions = null;
        InstanceSectionScreen.Opacity = 1;
        translation.X = GetSectionSlideDistance();
        InstanceSectionScreen.IsVisible = true;
        InstanceSectionScreen.Transitions = transitions;
        translation.Transitions = translationTransitions;
    }

    private double GetSectionSlideDistance()
        => Math.Max(1, InstancesContent.Bounds.Width > 0 ? InstancesContent.Bounds.Width : Bounds.Width);

    private void SetSectionTranslationDuration(TimeSpan duration)
    {
        var translation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        var transition = translation.Transitions?.OfType<DoubleTransition>().FirstOrDefault();
        if (transition is not null)
            transition.Duration = duration;
    }

    private void PrepareSectionForEntry(TranslateTransform translation)
    {
        var transitions = InstanceSectionScreen.Transitions;
        var translationTransitions = translation.Transitions;
        InstanceSectionScreen.Transitions = null;
        translation.Transitions = null;
        InstanceSectionScreen.Opacity = 0;
        translation.X = 28;
        InstanceSectionScreen.IsVisible = true;
        InstanceSectionScreen.Transitions = transitions;
        translation.Transitions = translationTransitions;
    }

    private void PrepareHubForEntry(TranslateTransform translation)
    {
        var transitions = InstanceHubScreen.Transitions;
        var translationTransitions = translation.Transitions;
        InstanceHubScreen.Transitions = null;
        translation.Transitions = null;
        InstanceHubScreen.Opacity = 0;
        translation.X = -28;
        InstanceHubScreen.IsVisible = true;
        InstanceHubScreen.Transitions = transitions;
        translation.Transitions = translationTransitions;
    }

    private void CancelSectionAnimation()
    {
        _sectionAnimationCancellation?.Cancel();
        _sectionAnimationCancellation?.Dispose();
        _sectionAnimationCancellation = null;
    }

    private void OnBranchSwitchSizeChanged(object? sender, SizeChangedEventArgs args)
        => UpdateBranchIndicator(animate: false);

    private void UpdateBranchIndicator(bool animate)
    {
        if (DataContext is not InstancesViewModel viewModel || BranchSwitchTrack.Bounds.Width <= 0)
            return;

        var translation = (TranslateTransform)BranchSelectionIndicator.RenderTransform!;
        var transitions = translation.Transitions;
        if (!animate)
            translation.Transitions = null;

        translation.X = viewModel.IsCreatePreReleaseBranch
            ? BranchSwitchTrack.Bounds.Width / 2
            : 0;

        if (!animate)
            translation.Transitions = transitions;
    }

    private void ApplyVersionLoadingStateImmediately()
    {
        CancelVersionLoadingAnimation();
        var isLoading = DataContext is InstancesViewModel { IsInstanceVersionsLoading: true };
        var comboTransitions = InstanceVersionComboBox.Transitions;
        var spinnerTransitions = VersionLoadingSpinner.Transitions;
        InstanceVersionComboBox.Transitions = null;
        VersionLoadingSpinner.Transitions = null;
        InstanceVersionComboBox.Opacity = isLoading ? 0 : 1;
        InstanceVersionComboBox.IsHitTestVisible = !isLoading;
        VersionLoadingSpinner.IsVisible = isLoading;
        VersionLoadingSpinner.Opacity = isLoading ? 1 : 0;
        InstanceVersionComboBox.Transitions = comboTransitions;
        VersionLoadingSpinner.Transitions = spinnerTransitions;
    }

    private void ShowVersionLoading()
    {
        CancelVersionLoadingAnimation();
        InstanceVersionComboBox.IsHitTestVisible = false;
        InstanceVersionComboBox.Opacity = 0;
        VersionLoadingSpinner.IsVisible = true;
        VersionLoadingSpinner.Opacity = 1;
    }

    private async Task HideVersionLoadingAsync()
    {
        CancelVersionLoadingAnimation();
        _versionLoadingCancellation = new CancellationTokenSource();
        var cancellationToken = _versionLoadingCancellation.Token;
        VersionLoadingSpinner.Opacity = 0;

        try
        {
            await Task.Delay(VersionLoadingFadeDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is InstancesViewModel { IsInstanceVersionsLoading: true })
            {
                return;
            }

            VersionLoadingSpinner.IsVisible = false;
            InstanceVersionComboBox.IsHitTestVisible = true;
            InstanceVersionComboBox.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A new loading cycle replaces the pending transition
        }
    }

    private void CancelVersionLoadingAnimation()
    {
        _versionLoadingCancellation?.Cancel();
        _versionLoadingCancellation?.Dispose();
        _versionLoadingCancellation = null;
    }

    private void ApplyModCatalogLoadingStateImmediately()
    {
        CancelModCatalogLoadingAnimation();
        var isLoading = DataContext is InstancesViewModel { IsModCatalogLoading: true };
        var listTransitions = ModCatalogList.Transitions;
        var spinnerTransitions = ModCatalogSearchSpinner.Transitions;
        ModCatalogList.Transitions = null;
        ModCatalogSearchSpinner.Transitions = null;
        ModCatalogList.Opacity = isLoading ? 0 : 1;
        ModCatalogList.IsHitTestVisible = !isLoading;
        ModCatalogSearchSpinner.IsVisible = isLoading;
        ModCatalogSearchSpinner.Opacity = isLoading ? 1 : 0;
        ModCatalogList.Transitions = listTransitions;
        ModCatalogSearchSpinner.Transitions = spinnerTransitions;
    }

    private void ShowModCatalogLoading()
    {
        CancelModCatalogLoadingAnimation();
        ModCatalogList.IsHitTestVisible = false;
        ModCatalogList.Opacity = 0;
        ModCatalogSearchSpinner.IsVisible = true;
        ModCatalogSearchSpinner.Opacity = 0;
        _modCatalogLoadingCancellation = new CancellationTokenSource();
        _ = FadeInModCatalogSpinnerAsync(_modCatalogLoadingCancellation.Token);
    }

    private async Task FadeInModCatalogSpinnerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ModCatalogSearchFadeDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not InstancesViewModel { IsModCatalogLoading: true })
            {
                return;
            }

            ModCatalogSearchSpinner.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A completed search or a new search replaces the pending reveal
        }
    }

    private async Task HideModCatalogLoadingAsync()
    {
        CancelModCatalogLoadingAnimation();
        _modCatalogLoadingCancellation = new CancellationTokenSource();
        var cancellationToken = _modCatalogLoadingCancellation.Token;
        ModCatalogSearchSpinner.Opacity = 0;

        try
        {
            await Task.Delay(ModCatalogSearchFadeDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is InstancesViewModel { IsModCatalogLoading: true })
            {
                return;
            }

            ModCatalogSearchSpinner.IsVisible = false;
            ModCatalogList.IsHitTestVisible = true;
            ModCatalogList.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A new search replaces the pending hide
        }
    }

    private void CancelModCatalogLoadingAnimation()
    {
        _modCatalogLoadingCancellation?.Cancel();
        _modCatalogLoadingCancellation?.Dispose();
        _modCatalogLoadingCancellation = null;
    }

    private static bool IsModArchiveName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName) &&
           (fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase));

    private void OnModFilesDragEntered(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        if (DataContext is not InstancesViewModel { IsManagedInstanceInstalled: true } ||
            !ContainsFiles(args.DataTransfer))
        {
            args.DragEffects = DragDropEffects.None;
            ShowModDropOverlay(false);
            return;
        }

        args.DragEffects = DragDropEffects.Copy;
        ShowModDropOverlay(true);
    }

    private void OnModFilesDragLeft(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        ShowModDropOverlay(false);
    }

    private async void OnModFilesDropped(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        ShowModDropOverlay(false);
        if (DataContext is not InstancesViewModel viewModel ||
            args.DataTransfer is not IAsyncDataTransfer data ||
            !ContainsFiles(args.DataTransfer))
        {
            return;
        }

        try
        {
            var files = await data.TryGetFilesAsync() ?? [];
            var paths = files
                .Select(file => file.TryGetLocalPath())
                .OfType<string>()
                .Where(IsModArchiveName)
                .ToList();
            await viewModel.ImportModFilesAsync(paths);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed drop keeps the mod list untouched
        }
    }

    private static bool ContainsFiles(IDataTransfer data)
        => data.Formats is { } formats && formats.Contains(DataFormat.File);

    private void ShowModDropOverlay(bool visible)
    {
        if (_modDropActive == visible)
            return;

        _modDropActive = visible;
        ModDropOverlay.IsVisible = visible;
    }

    private void OnCatalogSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Return) ||
            DataContext is not InstancesViewModel viewModel)
        {
            return;
        }

        args.Handled = true;
        viewModel.SearchModCatalogCommand.Execute(null);
    }

    private void OnCatalogModPreviewRequested(object? sender, TappedEventArgs args)
    {
        if (sender is not Border { DataContext: ModCatalogItemViewModel item } ||
            DataContext is not InstancesViewModel viewModel)
        {
            return;
        }

        if (args.Source is Button or CheckBox ||
            args.Source is Visual visual &&
            (visual.FindAncestorOfType<Button>() is not null ||
             visual.FindAncestorOfType<CheckBox>() is not null))
        {
            return;
        }

        viewModel.SelectModCatalogPreviewCommand.Execute(item);
    }

    private void OnModCatalogScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.Y >= 0 ||
            sender is not ScrollViewer scrollViewer ||
            DataContext is not InstancesViewModel viewModel ||
            !viewModel.CanLoadMoreModCatalog)
        {
            return;
        }

        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >=
            scrollViewer.Extent.Height - 220)
        {
            viewModel.LoadMoreModCatalogCommand.Execute(null);
        }
    }

    private void OnLogsScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.Y == 0 ||
            sender is not ScrollViewer scrollViewer ||
            DataContext is not InstancesViewModel viewModel ||
            !viewModel.IsLogsAutoScroll)
        {
            return;
        }

        var distanceFromBottom = scrollViewer.Extent.Height -
                                 scrollViewer.Offset.Y -
                                 scrollViewer.Viewport.Height;
        if (distanceFromBottom > 24)
            viewModel.IsLogsAutoScroll = false;
    }

    private void OnLogsContentPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Handled || DataContext is not InstancesViewModel viewModel)
            return;

        if (args.Source is Visual source &&
            (source is ListBoxItem || source.GetVisualAncestors().OfType<ListBoxItem>().Any()))
        {
            return;
        }

        viewModel.LogsSearchQuery = string.Empty;
        LogsContentHost.Focus();
    }

    private void OnLogsSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Escape || DataContext is not InstancesViewModel viewModel)
            return;

        viewModel.LogsSearchQuery = string.Empty;
        LogsContentHost.Focus();
        args.Handled = true;
    }

    private async void OnLogsListKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Source is TextBox || args.Key != Key.C ||
            (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 ||
            LogsList.SelectedItems is null)
        {
            return;
        }

        var textBlock = args.Source as SelectableTextBlock ??
            (args.Source as Visual)?.FindAncestorOfType<SelectableTextBlock>();
        if (textBlock is not null && !string.IsNullOrEmpty(textBlock.SelectedText))
        {
            return;
        }

        var lines = LogsList.SelectedItems.OfType<InstanceLogLineViewModel>()
            .Select(line => string.Join("\t", line.Time, line.Level, line.Source, line.Text));
        var text = string.Join(Environment.NewLine, lines);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (text.Length == 0 || clipboard is null)
            return;

        await clipboard.SetTextAsync(text);
        args.Handled = true;
    }

    private void ScrollLogsToBottom()
    {
        if (DataContext is not InstancesViewModel { IsLogsAutoScroll: true } viewModel ||
            viewModel.LogsLines.Count == 0)
        {
            return;
        }

        var scrollViewer = LogsList.GetVisualDescendants().OfType<SmoothScrollViewer>().FirstOrDefault();
        if (scrollViewer is not null)
        {
            scrollViewer.Offset = new Vector(
                scrollViewer.Offset.X,
                Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height));
            return;
        }

        LogsList.ScrollIntoView(viewModel.LogsLines[^1]);
    }

    private void OnCloseModDeleteFlyoutClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is not Control control)
            return;

        var popup = control.FindAncestorOfType<Popup>() ??
            control.GetLogicalAncestors().OfType<Popup>().FirstOrDefault();
        if (popup is not null)
            popup.IsOpen = false;
    }

}
