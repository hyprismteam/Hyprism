// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstancesView : UserControl
{
    private static readonly TimeSpan CompactContentTransitionDuration = MotionDurations.CompactPageSlide;
    private static readonly TimeSpan CompactSectionSlideDuration = MotionDurations.CompactSectionSlide;
    private static readonly TimeSpan WideSectionSlideDuration = MotionDurations.ContentFade;
    private readonly WizardHost _creatorWizard;
    private readonly AdaptiveMasterDetailHost _layoutHost;
    private readonly ReorderableListController _instanceReorder;
    private INotifyPropertyChanged? _viewModel;
    private bool _creatorOpenedFromCompactList;
    private bool _creatorTransitionActive;
    private int _creatorNavigationRevision;
    private CancellationTokenSource? _sectionAnimationCancellation;

    private Grid InstanceHubScreen => InstanceOverviewContentView.HubScreen;
    private StackPanel InstanceHubContent => InstanceOverviewContentView.HubContent;
    private Border CompactInstanceToolbar => InstanceOverviewContentView.CompactToolbar;

    public InstancesView()
    {
        InitializeComponent();
        InstanceOverviewContentView.BackRequested += OnCompactInstanceBackClicked;
        InstanceListContentView.InstanceClicked += OnInstanceClicked;
        InstanceListContentView.CreateRequested += OnOpenCreatorClicked;
        InstanceListContentView.DragHandlePressed += OnInstanceDragHandlePressed;
        InstanceListContentView.DragHandleMoved += OnInstanceDragHandleMoved;
        InstanceListContentView.DragHandleReleased += OnInstanceDragHandleReleased;
        _creatorWizard = new WizardHost(
            InstancesOverview,
            InstanceCreatorScreen,
            InstanceListContentView.Rail,
            InstanceCreatorContentView.Reveal.Anchor,
            InstanceCreatorContentView.Reveal.MotionTarget,
            InstanceCreatorContentView.Reveal.Animation);
        _layoutHost = new AdaptiveMasterDetailHost(
            InstancesLayout,
            InstanceListContentView.Rail,
            InstancesContent,
            CompactInstanceToolbar,
            InstanceHubContent,
            compact =>
            {
                Classes.Set("compact", compact);
                Classes.Set("wide", !compact);
            });
        _instanceReorder = new ReorderableListController(
            InstanceListContentView.Items,
            InstancesLayout,
            InstanceDragPreview,
            InstanceListContentView.Rail);
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
        ApplySectionStateImmediately();

        if (DataContext is InstancesViewModel { IsInstanceCreatorOpen: true })
            _ = PlayCreatorOpenAnimationAsync();
        else
            HideCreatorImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstancesViewModel.HasInstances))
            UpdateLayout(Bounds.Width);

        if (args.PropertyName is nameof(InstancesViewModel.InstanceSection))
        {
            if (DataContext is InstancesViewModel { IsInstanceOverviewSection: true })
                _ = PlaySectionCloseAnimationAsync();
            else
                _ = PlaySectionOpenAnimationAsync();
        }

        if (args.PropertyName is nameof(InstancesViewModel.IsInstanceCreatorOpen))
        {
            if (DataContext is InstancesViewModel { IsInstanceCreatorOpen: true })
                _ = PlayCreatorOpenAnimationAsync();
            else
                _ = PlayCreatorCloseAnimationAsync();
        }

    }

    private void OnModCatalogModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.CompleteModCatalogPreviewClose();
    }

    private void OnModCatalogInstallModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.CompleteModCatalogInstallConfirmationClose();
    }

    private void OnInstanceDeleteModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.CompleteManagedInstanceDeletionClose();
    }

    private void OnInstanceEditModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.CompleteInstanceEditClose();
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
        Grid.SetRow(InstanceListContentView, 0);
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
        var catalogMaxWidth = _layoutHost.IsCompact
            ? double.PositiveInfinity
            : InstanceModsView.ModCatalogContentMaxWidth;
        InstanceModsContentView.SetMaximumWidth(maxWidth, catalogMaxWidth);
        InstanceLogsContentView.SetMaximumWidth(catalogMaxWidth);
    }

    private void OnInstanceClicked(object? sender, RoutedEventArgs args)
    {
        _layoutHost.RememberDetail();
        if (!_layoutHost.IsCompact)
            return;

        _layoutHost.OpenDetail();
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
                InstanceCreatorContentView.RefreshBranchIndicator();
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
                InstanceCreatorContentView.RefreshBranchIndicator);
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

}
