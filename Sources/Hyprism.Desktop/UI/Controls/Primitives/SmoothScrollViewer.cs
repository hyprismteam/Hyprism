// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Hyprism.Desktop.Controls;

/// <summary>
/// A scroll viewer with eased wheel scrolling and optional browser-style middle-click auto-scroll.
/// </summary>
public sealed class SmoothScrollViewer : ScrollViewer
{
    public static readonly StyledProperty<bool> IsPastTopProperty =
        AvaloniaProperty.Register<SmoothScrollViewer, bool>(
            nameof(IsPastTop),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> ScrollContextKeyProperty =
        AvaloniaProperty.Register<SmoothScrollViewer, string?>(nameof(ScrollContextKey));

    public static readonly StyledProperty<bool> EnableMiddleClickAutoScrollProperty =
        AvaloniaProperty.Register<SmoothScrollViewer, bool>(
            nameof(EnableMiddleClickAutoScroll),
            defaultValue: false);

    public static readonly StyledProperty<bool> IsWheelEasingEnabledProperty =
        AvaloniaProperty.Register<SmoothScrollViewer, bool>(
            nameof(IsWheelEasingEnabled),
            defaultValue: true);

    private const double WheelStep = 92;
    private const double WheelEasingPerTick = 0.16;
    private const double TickMilliseconds = 16.0;
    private const double AutoScrollDeadZone = 14;
    private const double AutoScrollAccelerationPerTick = 0.14;
    private const double AutoScrollMaximumVelocity = 1625;
    private const double AutoScrollBaseVelocity = 137.5;
    private const double AutoScrollStopVelocity = 1.25;
    private const double AutoScrollEasingExponent = 1.12;
    private const double AutoScrollEasingScale = 18;
    private readonly Cursor _autoScrollIdleCursor = new(StandardCursorType.SizeAll);
    private readonly Cursor _autoScrollUpCursor = new(StandardCursorType.TopSide);
    private readonly Cursor _autoScrollDownCursor = new(StandardCursorType.BottomSide);
    private double _targetX;
    private double _targetY;
    private bool _isAnimating;
    private bool _isAutoScrolling;
    private bool _isFrameLoopActive;
    private TimeSpan? _lastFrameTimestamp;
    private Point _autoScrollAnchor;
    private double _autoScrollVelocity;
    private double _autoScrollTargetVelocity;
    private Cursor? _previousCursor;
    private IPointer? _capturedPointer;
    private bool _isApplyingOffset;

    public SmoothScrollViewer()
    {
        // Focus changes must never move the page implicitly. Scrolling remains
        // an explicit pointer or keyboard action.
        BringIntoViewOnFocusChange = false;
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public bool IsPastTop
    {
        get => GetValue(IsPastTopProperty);
        set => SetValue(IsPastTopProperty, value);
    }

    public string? ScrollContextKey
    {
        get => GetValue(ScrollContextKeyProperty);
        set => SetValue(ScrollContextKeyProperty, value);
    }

    public bool EnableMiddleClickAutoScroll
    {
        get => GetValue(EnableMiddleClickAutoScrollProperty);
        set => SetValue(EnableMiddleClickAutoScrollProperty, value);
    }

    public bool IsWheelEasingEnabled
    {
        get => GetValue(IsWheelEasingEnabledProperty);
        set => SetValue(IsWheelEasingEnabledProperty, value);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        => HandlePointerWheelChanged(e);

    internal void HandlePointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!IsWheelEasingEnabled)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        var maxX = Math.Max(0, Extent.Width - Viewport.Width);
        var maxY = Math.Max(0, Extent.Height - Viewport.Height);
        var wheelDelta = e.Delta;
        if ((maxX <= 0 && maxY <= 0) ||
            (Math.Abs(wheelDelta.X) < double.Epsilon && Math.Abs(wheelDelta.Y) < double.Epsilon))
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        var currentTargetX = _isAnimating ? _targetX : Offset.X;
        var currentTargetY = _isAnimating ? _targetY : Offset.Y;
        var targetX = Math.Clamp(currentTargetX - wheelDelta.X * WheelStep, 0, maxX);
        var targetY = Math.Clamp(currentTargetY - wheelDelta.Y * WheelStep, 0, maxY);
        var canScrollHorizontally = Math.Abs(wheelDelta.X) >= double.Epsilon &&
                                    maxX > 0 &&
                                    !IsAtScrollBoundary(wheelDelta.X, Offset.X, currentTargetX, maxX);
        var canScrollVertically = Math.Abs(wheelDelta.Y) >= double.Epsilon &&
                                  maxY > 0 &&
                                  !IsAtScrollBoundary(wheelDelta.Y, Offset.Y, currentTargetY, maxY);
        if (!canScrollHorizontally && !canScrollVertically)
        {
            _isAnimating = false;
            _targetX = Offset.X;
            _targetY = Offset.Y;
            base.OnPointerWheelChanged(e);
            return;
        }

        StopAutoScroll();
        _targetX = targetX;
        _targetY = targetY;
        _isAnimating = true;
        EnsureFrameLoop();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (EnableMiddleClickAutoScroll && point.Properties.IsMiddleButtonPressed)
        {
            if (_isAutoScrolling)
            {
                StopAutoScroll();
            }
            else if (Extent.Height > Viewport.Height)
            {
                _isAnimating = false;
                _isAutoScrolling = true;
                _autoScrollAnchor = point.Position;
                _autoScrollVelocity = 0;
                _autoScrollTargetVelocity = 0;
                _targetY = Offset.Y;
                _previousCursor = Cursor;
                Cursor = _autoScrollIdleCursor;
                _capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
                EnsureFrameLoop();
            }

            e.Handled = true;
            return;
        }

        if (_isAutoScrolling)
            StopAutoScroll();

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_isAutoScrolling)
        {
            var delta = e.GetPosition(this).Y - _autoScrollAnchor.Y;
            var distance = Math.Max(0, Math.Abs(delta) - AutoScrollDeadZone);
            _autoScrollTargetVelocity = distance == 0
                ? 0
                : Math.CopySign(
                    Math.Min(
                        AutoScrollMaximumVelocity,
                        Math.Pow(distance / AutoScrollEasingScale, AutoScrollEasingExponent) * AutoScrollBaseVelocity),
                    delta);
            UpdateAutoScrollCursor();
            e.Handled = true;
            return;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_isAutoScrolling && e.Key == Key.Escape)
        {
            StopAutoScroll();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopAutoScroll();
        _isAnimating = false;
        _isFrameLoopActive = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OffsetProperty)
        {
            if (!_isApplyingOffset)
            {
                _isAnimating = false;
                var offset = change.GetNewValue<Vector>();
                _targetX = offset.X;
                _targetY = offset.Y;
            }

            SetCurrentValue(IsPastTopProperty, Offset.Y > 6);
        }
        else if (change.Property == ScrollContextKeyProperty)
        {
            ResetScrollPosition();
        }
    }

    private void EnsureFrameLoop()
    {
        if (_isFrameLoopActive)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        _isFrameLoopActive = true;
        _lastFrameTimestamp = null;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        if (!_isFrameLoopActive)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || !this.IsAttachedToVisualTree())
        {
            _isFrameLoopActive = false;
            return;
        }

        var deltaMilliseconds = _lastFrameTimestamp is { } previous
            ? (timestamp - previous).TotalMilliseconds
            : 0.0;
        _lastFrameTimestamp = timestamp;

        var maxX = Math.Max(0, Extent.Width - Viewport.Width);
        var maxY = Math.Max(0, Extent.Height - Viewport.Height);
        if (_isAutoScrolling)
        {
            _autoScrollVelocity += (_autoScrollTargetVelocity - _autoScrollVelocity) *
                                   EasePerFrame(AutoScrollAccelerationPerTick, deltaMilliseconds);
            if (Math.Abs(_autoScrollVelocity) < AutoScrollStopVelocity && _autoScrollTargetVelocity == 0)
                _autoScrollVelocity = 0;

            var next = Math.Clamp(Offset.Y + _autoScrollVelocity * deltaMilliseconds / 1000.0, 0, maxY);
            SetAnimatedOffset(new Vector(Offset.X, next));
            if ((next <= 0 && _autoScrollVelocity < 0) ||
                (next >= maxY && _autoScrollVelocity > 0))
            {
                _autoScrollVelocity = 0;
                _autoScrollTargetVelocity = 0;
                Cursor = _autoScrollIdleCursor;
            }
        }

        if (_isAnimating)
        {
            _targetX = Math.Clamp(_targetX, 0, maxX);
            _targetY = Math.Clamp(_targetY, 0, maxY);
            var deltaX = _targetX - Offset.X;
            var deltaY = _targetY - Offset.Y;
            if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5)
            {
                SetAnimatedOffset(new Vector(_targetX, _targetY));
                _isAnimating = false;
            }
            else
            {
                var easing = EasePerFrame(WheelEasingPerTick, deltaMilliseconds);
                SetAnimatedOffset(new Vector(
                    Offset.X + deltaX * easing,
                    Offset.Y + deltaY * easing));
            }
        }

        if (!_isAnimating && !_isAutoScrolling)
        {
            _isFrameLoopActive = false;
            return;
        }

        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private static double EasePerFrame(double perTickFactor, double deltaMilliseconds)
        => 1 - Math.Pow(1 - perTickFactor, Math.Max(0, deltaMilliseconds) / TickMilliseconds);

    private static bool IsAtScrollBoundary(
        double wheelDelta,
        double offset,
        double target,
        double maximum)
        => (wheelDelta > 0 && offset <= 0 && target <= 0) ||
           (wheelDelta < 0 && offset >= maximum && target >= maximum);

    private void SetAnimatedOffset(Vector offset)
    {
        _isApplyingOffset = true;
        try
        {
            Offset = offset;
        }
        finally
        {
            _isApplyingOffset = false;
        }
    }

    private void ResetScrollPosition()
    {
        _isAnimating = false;
        StopAutoScroll();
        _targetX = 0;
        _targetY = 0;
        Offset = new Vector(0, 0);
        SetCurrentValue(IsPastTopProperty, false);
    }

    private void StopAutoScroll()
    {
        if (!_isAutoScrolling)
            return;

        _isAutoScrolling = false;
        _autoScrollVelocity = 0;
        _autoScrollTargetVelocity = 0;
        Cursor = _previousCursor;
        _previousCursor = null;
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
    }

    private void UpdateAutoScrollCursor()
    {
        if (_autoScrollTargetVelocity < 0)
        {
            Cursor = _autoScrollUpCursor;
        }
        else if (_autoScrollTargetVelocity > 0)
        {
            Cursor = _autoScrollDownCursor;
        }
        else
        {
            Cursor = _autoScrollIdleCursor;
        }
    }
}
