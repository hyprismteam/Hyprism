// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hyprism.Desktop.Controls;

public sealed class FadingPopup : Popup
{
    private static readonly TimeSpan CloseRetentionDuration = MotionDurations.PopupCloseRetention;

    public static readonly StyledProperty<bool> IsRequestedOpenProperty =
        AvaloniaProperty.Register<FadingPopup, bool>(nameof(IsRequestedOpen));

    public static readonly StyledProperty<bool> IsHoverEnabledProperty =
        AvaloniaProperty.Register<FadingPopup, bool>(nameof(IsHoverEnabled));

    public static readonly StyledProperty<double> PlacementGapProperty =
        AvaloniaProperty.Register<FadingPopup, double>(nameof(PlacementGap));

    public static readonly StyledProperty<bool> ConstrainToPlacementContextProperty =
        AvaloniaProperty.Register<FadingPopup, bool>(nameof(ConstrainToPlacementContext));

    private CancellationTokenSource? _animationCancellation;
    private TopLevel? _subscribedTopLevel;
    private Window? _subscribedWindow;
    private readonly List<ScrollViewer> _subscribedScrollViewers = [];
    private Control? _hoverTarget;
    private Control? _hoverChild;
    private Control? _placementChild;
    private RectangleGeometry? _contextClip;
    private Rect? _lastTargetRect;
    private Size? _lastTopLevelSize;
    private bool _isRefreshingPlacement;
    private bool? _placeAbove;

    public FadingPopup()
    {
        IsLightDismissEnabled = false;
        WindowManagerAddShadowHint = false;
        ShouldUseOverlayLayer = true;
    }

    public bool IsRequestedOpen
    {
        get => GetValue(IsRequestedOpenProperty);
        set => SetValue(IsRequestedOpenProperty, value);
    }

    public bool IsHoverEnabled
    {
        get => GetValue(IsHoverEnabledProperty);
        set => SetValue(IsHoverEnabledProperty, value);
    }

    public double PlacementGap
    {
        get => GetValue(PlacementGapProperty);
        set => SetValue(PlacementGapProperty, value);
    }

    public bool ConstrainToPlacementContext
    {
        get => GetValue(ConstrainToPlacementContextProperty);
        set => SetValue(ConstrainToPlacementContextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsRequestedOpenProperty)
        {
            if (change.Property == PlacementTargetProperty ||
                change.Property == ChildProperty ||
                change.Property == IsHoverEnabledProperty ||
                change.Property == PlacementGapProperty ||
                change.Property == ConstrainToPlacementContextProperty)
            {
                if (change.Property != IsHoverEnabledProperty)
                    _placeAbove = null;

                RefreshHoverSubscriptions();
                RefreshPlacementSubscriptions();
                UpdatePlacementContext();
            }

            if (change.Property == IsOpenProperty)
                RefreshPlacementSubscriptions();

            return;
        }

        if (change.GetNewValue<bool>())
            ShowPopup();
        else
            BeginHidePopup();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshHoverSubscriptions();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearHoverSubscriptions();
        ClearPlacementSubscriptions();
        ClearPlacementContextClip();
        CancelPendingAnimation();
        UnsubscribeFromTopLevel();
        SetCurrentValue(IsOpenProperty, false);
        if (Child is not null)
            Child.Opacity = 1;
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshHoverSubscriptions()
    {
        if (!IsHoverEnabled)
        {
            ClearHoverSubscriptions();
            return;
        }

        var target = PlacementTarget as Control;
        if (!ReferenceEquals(target, _hoverTarget))
        {
            if (_hoverTarget is not null)
            {
                _hoverTarget.PointerEntered -= OnHoverPointerEntered;
                _hoverTarget.PointerExited -= OnHoverPointerExited;
            }

            _hoverTarget = target;
            if (_hoverTarget is not null)
            {
                _hoverTarget.PointerEntered += OnHoverPointerEntered;
                _hoverTarget.PointerExited += OnHoverPointerExited;
            }
        }

        var child = Child;
        if (!ReferenceEquals(child, _hoverChild))
        {
            if (_hoverChild is not null)
            {
                _hoverChild.PointerEntered -= OnHoverPointerEntered;
                _hoverChild.PointerExited -= OnHoverPointerExited;
            }

            _hoverChild = child;
            if (_hoverChild is not null)
            {
                _hoverChild.PointerEntered += OnHoverPointerEntered;
                _hoverChild.PointerExited += OnHoverPointerExited;
            }
        }
    }

    private void ClearHoverSubscriptions()
    {
        if (_hoverTarget is not null)
        {
            _hoverTarget.PointerEntered -= OnHoverPointerEntered;
            _hoverTarget.PointerExited -= OnHoverPointerExited;
        }

        if (_hoverChild is not null)
        {
            _hoverChild.PointerEntered -= OnHoverPointerEntered;
            _hoverChild.PointerExited -= OnHoverPointerExited;
        }

        _hoverTarget = null;
        _hoverChild = null;
    }

    private void RefreshPlacementSubscriptions()
    {
        ClearPlacementSubscriptions();

        if (PlacementGap <= 0 || !IsOpen || Child is not Control child)
            return;

        _placementChild = child;
        _placementChild.LayoutUpdated += OnPlacementChildLayoutUpdated;
    }

    private void ClearPlacementSubscriptions()
    {
        if (_placementChild is not null)
            _placementChild.LayoutUpdated -= OnPlacementChildLayoutUpdated;

        _placementChild = null;
    }

    private void OnHoverPointerEntered(object? sender, PointerEventArgs args)
        => SetCurrentValue(IsRequestedOpenProperty, true);

    private void OnHoverPointerExited(object? sender, PointerEventArgs args)
        => Dispatcher.UIThread.Post(UpdateHoverState, DispatcherPriority.Input);

    private void UpdateHoverState()
    {
        if (!IsHoverEnabled)
            return;

        var isPointerOverHoverSurface = _hoverTarget?.IsPointerOver == true ||
                                        _hoverChild?.IsPointerOver == true;
        SetCurrentValue(IsRequestedOpenProperty, isPointerOverHoverSurface);
    }

    internal bool IsInteractionSource(Visual source)
    {
        if (PlacementTarget is Visual target &&
            (ReferenceEquals(source, target) || target.IsVisualAncestorOf(source)))
        {
            return true;
        }

        return Child is not null &&
               (ReferenceEquals(source, Child) || Child.IsVisualAncestorOf(source));
    }

    private void ShowPopup()
    {
        CancelPendingAnimation();
        _placeAbove = null;
        if (PlacementGap > 0)
        {
            SetCurrentValue(PlacementProperty, PlacementMode.Bottom);
            SetCurrentValue(VerticalOffsetProperty, PlacementGap);
        }

        if (Child is not null)
            Child.Opacity = 0;

        SetCurrentValue(IsOpenProperty, true);
        RefreshPlacementSubscriptions();
        SubscribeToTopLevel();
        UpdatePlacementGap();
        UpdatePlacementContext();
        _animationCancellation = new CancellationTokenSource();
        _ = PlayOpenAnimationAsync(_animationCancellation.Token);
    }

    private void BeginHidePopup()
    {
        ClearPlacementSubscriptions();
        ClearPlacementContextClip();
        UnsubscribeFromTopLevel();
        if (!IsOpen)
            return;

        CancelPendingAnimation();
        if (Child is not null)
            Child.Opacity = 0;

        _animationCancellation = new CancellationTokenSource();
        _ = PlayCloseAnimationAndHideAsync(_animationCancellation.Token);
    }

    private async Task PlayOpenAnimationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (!cancellationToken.IsCancellationRequested && IsRequestedOpen && Child is not null)
                Child.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A close request replaces the opening animation
            return;
        }
    }

    private async Task PlayCloseAnimationAndHideAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(CloseRetentionDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested || IsRequestedOpen)
                return;

            SetCurrentValue(IsOpenProperty, false);
            if (Child is not null)
                Child.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // Reopening the popup cancels the pending visual close
            return;
        }
    }

    private void SubscribeToTopLevel()
    {
        var topLevel = TopLevel.GetTopLevel(PlacementTarget ?? this);
        if (ReferenceEquals(topLevel, _subscribedTopLevel))
            return;

        UnsubscribeFromTopLevel();
        _subscribedTopLevel = topLevel;
        _subscribedTopLevel?.AddHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _subscribedTopLevel?.AddHandler(
            KeyDownEvent,
            OnTopLevelKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        if (_subscribedTopLevel is not null)
            _subscribedTopLevel.LayoutUpdated += OnTopLevelLayoutUpdated;

        if (topLevel is Window window)
        {
            _subscribedWindow = window;
            _subscribedWindow.Deactivated += OnWindowDeactivated;
        }

        SubscribeToScrollViewers();
    }

    private void UnsubscribeFromTopLevel()
    {
        _subscribedTopLevel?.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
        _subscribedTopLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        if (_subscribedTopLevel is not null)
            _subscribedTopLevel.LayoutUpdated -= OnTopLevelLayoutUpdated;
        if (_subscribedWindow is not null)
            _subscribedWindow.Deactivated -= OnWindowDeactivated;

        _subscribedTopLevel = null;
        _subscribedWindow = null;
        _lastTargetRect = null;
        _lastTopLevelSize = null;
        UnsubscribeFromScrollViewers();
    }

    private void SubscribeToScrollViewers()
    {
        UnsubscribeFromScrollViewers();

        if (PlacementTarget is not Visual target)
            return;

        foreach (var scrollViewer in target.GetSelfAndVisualAncestors().OfType<ScrollViewer>())
        {
            scrollViewer.ScrollChanged += OnScrollChanged;
            _subscribedScrollViewers.Add(scrollViewer);
        }
    }

    private void UnsubscribeFromScrollViewers()
    {
        foreach (var scrollViewer in _subscribedScrollViewers)
            scrollViewer.ScrollChanged -= OnScrollChanged;

        _subscribedScrollViewers.Clear();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (!IsOpen || !IsRequestedOpen)
            return;

        var offset = VerticalOffset;
        SetCurrentValue(VerticalOffsetProperty, offset + 0.001);
        SetCurrentValue(VerticalOffsetProperty, offset);
        UpdatePlacementContext();
    }

    private void OnPlacementChildLayoutUpdated(object? sender, EventArgs args)
    {
        UpdatePlacementGap();
        UpdatePlacementContext();
    }

    private void OnTopLevelLayoutUpdated(object? sender, EventArgs args)
    {
        if (_isRefreshingPlacement || !IsOpen || !IsRequestedOpen ||
            PlacementTarget is not Visual target ||
            _subscribedTopLevel is not { } topLevel)
        {
            UpdatePlacementContext();
            return;
        }

        var targetOrigin = target.TranslatePoint(new Point(), topLevel);
        if (targetOrigin is not { } targetPoint)
        {
            UpdatePlacementContext();
            return;
        }

        var targetRect = new Rect(targetPoint, target.Bounds.Size);
        var topLevelSize = topLevel.Bounds.Size;
        var topLevelResized = _lastTopLevelSize is not { } previousSize ||
                              previousSize != topLevelSize;
        var targetMoved = _lastTargetRect is not { } previousRect ||
                          previousRect != targetRect;
        _lastTargetRect = targetRect;
        _lastTopLevelSize = topLevelSize;

        if (!topLevelResized && !targetMoved)
        {
            UpdatePlacementContext();
            return;
        }

        if (topLevelResized)
            _placeAbove = null;

        RefreshPopupPlacement();
    }

    private void RefreshPopupPlacement()
    {
        if (_isRefreshingPlacement || !IsOpen || !IsRequestedOpen)
            return;

        _isRefreshingPlacement = true;
        try
        {
            UpdatePlacementGap();

            var horizontalOffset = HorizontalOffset;
            SetCurrentValue(HorizontalOffsetProperty, horizontalOffset + 0.001);
            SetCurrentValue(HorizontalOffsetProperty, horizontalOffset);

            var verticalOffset = VerticalOffset;
            SetCurrentValue(VerticalOffsetProperty, verticalOffset + 0.001);
            SetCurrentValue(VerticalOffsetProperty, verticalOffset);
            UpdatePlacementContext();
        }
        finally
        {
            _isRefreshingPlacement = false;
        }
    }

    private void UpdatePlacementGap()
    {
        if (!IsOpen || !IsRequestedOpen ||
            PlacementTarget is not Visual target ||
            Child is not Visual child ||
            PlacementGap <= 0)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(target);
        if (topLevel is null)
            return;

        var targetOrigin = target.TranslatePoint(new Point(), topLevel);
        if (targetOrigin is not { } targetPoint || child.Bounds.Height <= 0)
            return;

        if (_placeAbove is null)
        {
            var targetRect = new Rect(targetPoint, target.Bounds.Size);
            var contextRect = GetPlacementContextRect(target, topLevel);
            var availableAbove = targetRect.Top - contextRect.Top;
            var availableBelow = contextRect.Bottom - targetRect.Bottom;
            var requiredHeight = child.Bounds.Height + PlacementGap;
            var fitsAbove = availableAbove >= requiredHeight;
            var fitsBelow = availableBelow >= requiredHeight;
            _placeAbove = fitsAbove && !fitsBelow ||
                          !fitsBelow && !fitsAbove && availableAbove > availableBelow;
        }

        var desiredPlacement = _placeAbove.Value ? PlacementMode.Top : PlacementMode.Bottom;
        if (Placement != desiredPlacement)
            SetCurrentValue(PlacementProperty, desiredPlacement);

        var desiredOffset = _placeAbove.Value ? -PlacementGap : PlacementGap;
        if (Math.Abs(VerticalOffset - desiredOffset) > 0.001)
            SetCurrentValue(VerticalOffsetProperty, desiredOffset);
    }

    private void UpdatePlacementContext()
    {
        if (!ConstrainToPlacementContext || !IsOpen ||
            PlacementTarget is not Visual target ||
            Child is not Visual child)
        {
            ClearPlacementContextClip();
            return;
        }

        var topLevel = TopLevel.GetTopLevel(target);
        if (topLevel is null || child.Bounds.Width <= 0 || child.Bounds.Height <= 0)
            return;

        var contextRect = GetPlacementContextRect(target, topLevel);
        var childOrigin = child.TranslatePoint(new Point(), topLevel);
        if (childOrigin is not { } origin)
            return;

        var visibleRect = contextRect.Intersect(new Rect(origin, child.Bounds.Size));
        visibleRect = new Rect(
            visibleRect.X - origin.X,
            visibleRect.Y - origin.Y,
            visibleRect.Width,
            visibleRect.Height);
        visibleRect = visibleRect.Intersect(new Rect(child.Bounds.Size));

        _contextClip ??= new RectangleGeometry();
        _contextClip.Rect = visibleRect.Width > 0 && visibleRect.Height > 0
            ? visibleRect
            : new Rect();
        if (!ReferenceEquals(child.Clip, _contextClip))
            child.Clip = _contextClip;
    }

    private void ClearPlacementContextClip()
    {
        if (Child is Visual child && ReferenceEquals(child.Clip, _contextClip))
            child.Clip = null;

        _contextClip = null;
    }

    private static Rect GetPlacementContextRect(Visual target, TopLevel topLevel)
    {
        var contextRect = new Rect(topLevel.Bounds.Size);
        var ancestors = target.GetSelfAndVisualAncestors()
            .Where(ancestor => !ReferenceEquals(ancestor, target))
            .ToList();

        var scrollViewerIndex = ancestors.FindIndex(ancestor => ancestor is ScrollViewer);
        if (scrollViewerIndex >= 0 && ancestors[scrollViewerIndex] is Control scrollViewer)
        {
            var origin = scrollViewer.TranslatePoint(new Point(), topLevel);
            if (origin is { } point)
                contextRect = contextRect.Intersect(new Rect(point, scrollViewer.Bounds.Size));

            // The page owns the scroll viewport. Ignore clipped cards and rows
            // below it, but keep the first clipped page surface above it.
            var pageSurface = ancestors
                .Skip(scrollViewerIndex + 1)
                .OfType<Control>()
                .FirstOrDefault(control => control is not TopLevel && control.ClipToBounds);
            if (pageSurface is not null)
            {
                origin = pageSurface.TranslatePoint(new Point(), topLevel);
                if (origin is { } pagePoint)
                    contextRect = contextRect.Intersect(new Rect(pagePoint, pageSurface.Bounds.Size));
            }

            return contextRect;
        }

        var clippedAncestor = ancestors
            .OfType<Control>()
            .LastOrDefault(control => control is not TopLevel && control.ClipToBounds);
        if (clippedAncestor is not null)
        {
            var origin = clippedAncestor.TranslatePoint(new Point(), topLevel);
            if (origin is { } point)
                contextRect = contextRect.Intersect(new Rect(point, clippedAncestor.Bounds.Size));
        }

        return contextRect;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!IsRequestedOpen || args.Source is not Visual source || IsInteractionSource(source))
            return;

        SetCurrentValue(IsRequestedOpenProperty, false);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs args)
    {
        if (!IsRequestedOpen || args.Key != Key.Escape)
            return;

        SetCurrentValue(IsRequestedOpenProperty, false);
        args.Handled = true;
    }

    private void OnWindowDeactivated(object? sender, EventArgs args)
        => SetCurrentValue(IsRequestedOpenProperty, false);

    private void CancelPendingAnimation()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
    }
}
