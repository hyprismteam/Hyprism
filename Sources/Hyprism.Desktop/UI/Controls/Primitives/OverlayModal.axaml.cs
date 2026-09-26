// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hyprism.Desktop.Controls;

public sealed partial class OverlayModal : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<OverlayModal, bool>(nameof(IsOpen));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<OverlayModal, ICommand?>(nameof(DismissCommand));

    public static readonly StyledProperty<object?> ModalContentProperty =
        AvaloniaProperty.Register<OverlayModal, object?>(nameof(ModalContent));

    public static readonly StyledProperty<Control?> BackdropTargetProperty =
        AvaloniaProperty.Register<OverlayModal, Control?>(nameof(BackdropTarget));

    public static readonly StyledProperty<Control?> InitialFocusTargetProperty =
        AvaloniaProperty.Register<OverlayModal, Control?>(nameof(InitialFocusTarget));

    public static readonly StyledProperty<double> SheetMaxWidthProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(SheetMaxWidth), 720);

    public static readonly StyledProperty<double> SheetMaxHeightProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(SheetMaxHeight), 680);

    public static readonly StyledProperty<Thickness> SheetMarginProperty =
        AvaloniaProperty.Register<OverlayModal, Thickness>(nameof(SheetMargin), new Thickness(20, 20, 20, 0));

    public static readonly StyledProperty<double> ShoulderMaxWidthProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(ShoulderMaxWidth), 780);

    public static readonly StyledProperty<Thickness> ShoulderMarginProperty =
        AvaloniaProperty.Register<OverlayModal, Thickness>(nameof(ShoulderMargin), new Thickness(20, 0, 20, 0));

    public static readonly StyledProperty<double> HiddenOffsetProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(HiddenOffset), 720);

    private CancellationTokenSource? _animationCancellation;
    private Control? _activeBackdropTarget;
    private IInputElement? _restoreFocusElement;
    private IEffect? _previousBackdropEffect;
    private BlurEffect? _backdropBlurEffect;
    private bool _previousBackdropHitTestVisible;
    private bool _initialized;

    public OverlayModal()
    {
        InitializeComponent();
        _initialized = true;
        ApplyStateImmediately();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public event EventHandler? Closed;

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public object? ModalContent
    {
        get => GetValue(ModalContentProperty);
        set => SetValue(ModalContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the control that is blurred and blocked while this modal is open.
    /// </summary>
    public Control? BackdropTarget
    {
        get => GetValue(BackdropTargetProperty);
        set => SetValue(BackdropTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the control that receives focus when the modal opens.
    /// </summary>
    public Control? InitialFocusTarget
    {
        get => GetValue(InitialFocusTargetProperty);
        set => SetValue(InitialFocusTargetProperty, value);
    }

    public double SheetMaxWidth
    {
        get => GetValue(SheetMaxWidthProperty);
        set => SetValue(SheetMaxWidthProperty, value);
    }

    public double SheetMaxHeight
    {
        get => GetValue(SheetMaxHeightProperty);
        set => SetValue(SheetMaxHeightProperty, value);
    }

    public Thickness SheetMargin
    {
        get => GetValue(SheetMarginProperty);
        set => SetValue(SheetMarginProperty, value);
    }

    public double ShoulderMaxWidth
    {
        get => GetValue(ShoulderMaxWidthProperty);
        set => SetValue(ShoulderMaxWidthProperty, value);
    }

    public Thickness ShoulderMargin
    {
        get => GetValue(ShoulderMarginProperty);
        set => SetValue(ShoulderMarginProperty, value);
    }

    public double HiddenOffset
    {
        get => GetValue(HiddenOffsetProperty);
        set => SetValue(HiddenOffsetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!_initialized)
            return;

        if (change.Property == IsOpenProperty)
        {
            if (change.GetNewValue<bool>())
                _ = ShowAsync();
            else
                _ = HideAsync();
        }
        else if (change.Property == BackdropTargetProperty && IsOpen)
        {
            ActivateBackdrop();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelAnimation();
        RestoreBackdrop();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task ShowAsync()
    {
        var cancellationToken = ReplaceAnimationCancellation();
        _restoreFocusElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        ActivateBackdrop();
        OverlayModalBackdrop.Opacity = 0;
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = HiddenOffset;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = 0;
        IsVisible = true;
        IsHitTestVisible = true;

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (cancellationToken.IsCancellationRequested || !IsOpen)
            return;

        SyncShoulderScaleWithSheetTravel(opening: true);
        OverlayModalBackdrop.Opacity = 1;
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = 0;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = 1;
        FocusInitialTarget();
    }

    private async Task HideAsync()
    {
        if (!IsVisible)
            return;

        var cancellationToken = ReplaceAnimationCancellation();
        IsHitTestVisible = false;
        BeginBackdropClosing();
        OverlayModalBackdrop.Opacity = 0;
        SyncShoulderScaleWithSheetTravel(opening: false);
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = HiddenOffset;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = 0;

        try
        {
            await Task.Delay(MotionDurations.ModalCloseRetention, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (IsOpen)
            return;

        IsVisible = false;
        RestoreBackdrop();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyStateImmediately()
    {
        CancelAnimation();
        IsVisible = IsOpen;
        IsHitTestVisible = IsOpen;
        OverlayModalBackdrop.Opacity = IsOpen ? 1 : 0;
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = IsOpen ? 0 : HiddenOffset;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = IsOpen ? 1 : 0;
        if (IsOpen)
            ActivateBackdrop();
        else
            RestoreBackdrop();
    }

    private void ActivateBackdrop()
    {
        var target = BackdropTarget;
        if (target is null)
            return;

        if (!ReferenceEquals(_activeBackdropTarget, target))
        {
            RestoreBackdrop();
            _activeBackdropTarget = target;
            _previousBackdropHitTestVisible = target.IsHitTestVisible;
            _previousBackdropEffect = target.Effect;
            _restoreFocusElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

            _backdropBlurEffect = target.Effect as BlurEffect ?? CreateBackdropBlurEffect();
            if (!ReferenceEquals(target.Effect, _backdropBlurEffect))
                target.Effect = _backdropBlurEffect;
        }

        if (_backdropBlurEffect is not null)
            _backdropBlurEffect.Radius = 6;

        target.IsHitTestVisible = false;
    }

    private void BeginBackdropClosing()
    {
        if (_activeBackdropTarget is { } target)
            target.IsHitTestVisible = _previousBackdropHitTestVisible;

        if (_backdropBlurEffect is not null)
            _backdropBlurEffect.Radius = 0;
    }

    private static BlurEffect CreateBackdropBlurEffect()
    {
        var effect = new BlurEffect();
        effect.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = BlurEffect.RadiusProperty,
                Duration = MotionDurations.ContentFade,
                Easing = new CubicEaseOut()
            }
        };
        return effect;
    }

    private void RestoreBackdrop()
    {
        if (_activeBackdropTarget is { } target)
        {
            if (_backdropBlurEffect is not null)
                _backdropBlurEffect.Radius = 0;

            if (!ReferenceEquals(target.Effect, _previousBackdropEffect))
                target.Effect = _previousBackdropEffect;

            target.IsHitTestVisible = _previousBackdropHitTestVisible;
        }

        var focusElement = _restoreFocusElement;
        _activeBackdropTarget = null;
        _restoreFocusElement = null;
        _previousBackdropEffect = null;
        _backdropBlurEffect = null;

        if (focusElement is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (focusElement is Control control &&
                control.IsVisible &&
                control.IsEnabled &&
                control.Focusable)
            {
                control.Focus();
                return;
            }

            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(
                focusElement,
                NavigationMethod.Tab,
                KeyModifiers.None);
        }, DispatcherPriority.Loaded);
    }

    private void FocusInitialTarget()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsOpen)
                return;

            var target = InitialFocusTarget;
            if (target is { IsVisible: true, IsEnabled: true, Focusable: true })
                target.Focus();
            else
                Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Aligns the shoulder scale animation with the moment the sheet visually crosses
    /// the window edge, so the shoulders finish exactly when the sheet arrives or leaves.
    /// </summary>
    private void SyncShoulderScaleWithSheetTravel(bool opening)
    {
        if (OverlayModalShoulders.RenderTransform is not ScaleTransform transform ||
            transform.Transitions?.OfType<DoubleTransition>().FirstOrDefault() is not { } transition)
            return;

        var sheetHeight = OverlayModalSheet.Bounds.Height;
        var visibleFraction = sheetHeight <= 0
            ? 1
            : Math.Clamp(sheetHeight / HiddenOffset, 0, 1);

        if (opening)
        {
            var enterProgress = InverseCubicEaseInOut(1 - visibleFraction);
            transition.Delay = MotionDurations.ModalCloseRetention * enterProgress;
            transition.Duration = MotionDurations.ModalCloseRetention * (1 - enterProgress);
        }
        else
        {
            transition.Delay = TimeSpan.Zero;
            transition.Duration = MotionDurations.ModalCloseRetention * InverseCubicEaseInOut(visibleFraction);
        }
    }

    private static double InverseCubicEaseInOut(double value)
    {
        if (value <= 0)
            return 0;

        if (value >= 1)
            return 1;

        return value < 0.5
            ? Math.Cbrt(value / 4)
            : 1 - (Math.Cbrt(2 * (1 - value)) / 2);
    }

    private CancellationToken ReplaceAnimationCancellation()
    {
        CancelAnimation();
        _animationCancellation = new CancellationTokenSource();
        return _animationCancellation.Token;
    }

    private void CancelAnimation()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!TryDismiss())
            return;

        args.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !TryDismiss())
            return;

        args.Handled = true;
    }

    private bool TryDismiss()
    {
        if (!IsOpen || DismissCommand?.CanExecute(null) != true)
            return false;

        DismissCommand.Execute(null);
        return true;
    }
}
