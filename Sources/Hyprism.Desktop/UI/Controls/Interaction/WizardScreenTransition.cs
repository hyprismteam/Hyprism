// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Hyprism.Desktop.Controls;

/// <summary>
/// Coordinates the phased transition between a content overview and its wizard screen.
/// </summary>
public sealed class WizardScreenTransition
{
    public static readonly TimeSpan PhaseDuration = MotionDurations.WizardPhase;
    public static readonly TimeSpan AnchorMoveDuration = PhaseDuration;
    private static readonly TimeSpan CompactOverlaySlideDuration =
        MotionDurations.CompactSectionSlide;

    private readonly Control _overview;
    private readonly Control _wizard;
    private readonly Control? _navigationPane;
    private readonly Control? _layoutAnchor;
    private readonly Control? _layoutMotionTarget;
    private readonly Control? _layoutContainer;
    private readonly double _navigationPaneWidth;
    private CancellationTokenSource? _animationCancellation;
    private double? _anchorTargetY;
    private bool _isPlannedAnchorMove;
    private bool _isNavigationPaneVisible = true;
    private bool _suppressAnchorLayoutTracking;
    private double _plannedAnchorLayoutDelta;

    public WizardScreenTransition(
        Control overview,
        Control wizard,
        Control? navigationPane = null,
        Control? layoutAnchor = null,
        Control? layoutMotionTarget = null,
        double navigationPaneWidth = ManagerLayoutMetrics.RailWidth)
    {
        _overview = overview;
        _wizard = wizard;
        _navigationPane = navigationPane;
        _layoutAnchor = layoutAnchor;
        _layoutMotionTarget = layoutMotionTarget ?? layoutAnchor;
        _navigationPaneWidth = navigationPaneWidth;
        if (_layoutAnchor is not null)
        {
            _layoutAnchor.RenderTransform ??= new TranslateTransform();
            _layoutMotionTarget!.RenderTransform ??= new TranslateTransform();
            _layoutContainer = _layoutAnchor.Parent as Control ?? _wizard;
            _layoutContainer.PropertyChanged += OnLayoutContainerPropertyChanged;
            _wizard.SizeChanged += OnWizardSizeChanged;
        }
    }

    public async Task OpenAsync(Func<bool> shouldRemainOpen, Action? onOpened = null)
    {
        var cancellationToken = BeginAnimation();
        var overviewTranslation = GetTranslation(_overview);
        _overview.IsHitTestVisible = false;
        _wizard.IsHitTestVisible = false;

        try
        {
            _overview.Opacity = 0;
            overviewTranslation.X = -28;
            await Task.Delay(PhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            _overview.IsVisible = false;
            CollapseHiddenNavigationPane();
            PrepareForEntry(_wizard, 28);

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            _wizard.IsHitTestVisible = true;
            _wizard.Opacity = 1;
            GetTranslation(_wizard).X = 0;
            InitializeAnchorTarget();
            onOpened?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // A reverse navigation replaces the pending transition
        }
    }

    public async Task OpenCompactOverlayAsync(
        Func<bool> shouldRemainOpen,
        Action? onSlideStarted,
        Action? onOpened,
        double horizontalOffset)
    {
        var cancellationToken = BeginAnimation();
        _overview.IsHitTestVisible = false;

        try
        {
            PrepareOverlayEntryState(horizontalOffset);
            _wizard.IsHitTestVisible = true;
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            onSlideStarted?.Invoke();
            await RunCompactOverlaySlideAsync(
                GetTranslation(_wizard),
                0,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            InitializeAnchorTarget();
            onOpened?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // A reverse navigation replaces the pending transition
        }
    }

    public async Task CloseAsync(Func<bool> shouldRemainClosed, Action? onClosed = null)
    {
        var cancellationToken = BeginAnimation();
        if (!_wizard.IsVisible)
        {
            ShowOverviewImmediately();
            onClosed?.Invoke();
            return;
        }

        _wizard.IsHitTestVisible = false;
        _wizard.Opacity = 0;
        GetTranslation(_wizard).X = 28;

        try
        {
            await Task.Delay(PhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainClosed())
                return;

            _wizard.IsVisible = false;
            PrepareForEntry(_overview, -28);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainClosed())
                return;

            _overview.IsHitTestVisible = true;
            _overview.Opacity = 1;
            GetTranslation(_overview).X = 0;
            onClosed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Reopening the wizard replaces the pending close transition
        }
    }

    public async Task CloseCompactOverlayAsync(
        Func<bool> shouldRemainClosed,
        Action? onClosed,
        double horizontalOffset)
    {
        var cancellationToken = BeginAnimation();
        if (!_wizard.IsVisible)
        {
            ShowOverviewImmediately();
            onClosed?.Invoke();
            return;
        }

        RevealOverviewBehindWizard();
        _wizard.IsHitTestVisible = false;

        try
        {
            await RunCompactOverlaySlideAsync(
                GetTranslation(_wizard),
                horizontalOffset,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainClosed())
                return;

            ShowOverviewImmediately();
            onClosed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Reopening the wizard replaces the pending close transition
        }
    }

    public async Task<bool> SwitchStepAsync(
        Control outgoingStep,
        Control incomingStep,
        bool forward,
        Action switchStep,
        Func<bool> shouldRemainOpen)
    {
        var cancellationToken = BeginAnimation();
        InitializeAnchorTarget();
        BeginPlannedAnchorMove(outgoingStep, incomingStep);
        var stepSwitched = false;
        PrepareHiddenState(incomingStep, forward ? 28 : -28);
        outgoingStep.IsHitTestVisible = false;
        incomingStep.IsHitTestVisible = false;

        try
        {
            var outgoingOffset = forward ? -28 : 28;
            await RunStepAnimationAsync(
                outgoingStep,
                fromOpacity: 1,
                toOpacity: 0,
                fromOffset: 0,
                toOffset: outgoingOffset,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
            {
                if (shouldRemainOpen())
                    RestoreVisibleState(outgoingStep);
                return false;
            }

            switchStep();
            stepSwitched = true;
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
            {
                if (shouldRemainOpen())
                    RestoreVisibleState(incomingStep);
                return false;
            }

            var anchorMoveCompletion = ContinuePlannedAnchorMove(cancellationToken);
            await Task.WhenAll(
                RunStepAnimationAsync(
                    incomingStep,
                    fromOpacity: 0,
                    toOpacity: 1,
                    fromOffset: forward ? 28 : -28,
                    toOffset: 0,
                    cancellationToken),
                anchorMoveCompletion);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return false;

            incomingStep.IsHitTestVisible = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            // Keep the model's active step visible when an interrupted animation
            // does not also close the wizard
            if (shouldRemainOpen())
                RestoreVisibleState(stepSwitched ? incomingStep : outgoingStep);
            return false;
        }
        finally
        {
            FinishPlannedAnchorMove(cancellationToken);
        }
    }

    public void ShowOverviewImmediately()
        => ApplyImmediateState(showWizard: false);

    public void ShowWizardImmediately()
        => ApplyImmediateState(showWizard: true);

    public void ShowNavigationPane(bool animate)
        => ApplyNavigationPaneState(isVisible: true, animate);

    public void HideNavigationPane(bool animate)
        => ApplyNavigationPaneState(isVisible: false, animate);

    public void ResetNavigationPane()
    {
        if (_navigationPane is null)
            return;

        var translation = GetTranslation(_navigationPane);
        var paneTransitions = _navigationPane.Transitions;
        var translationTransitions = translation.Transitions;
        _navigationPane.Transitions = null;
        translation.Transitions = null;
        _navigationPane.Width = double.NaN;
        _navigationPane.Opacity = 1;
        translation.X = 0;
        _isNavigationPaneVisible = true;
        _navigationPane.Transitions = paneTransitions;
        translation.Transitions = translationTransitions;
    }

    public void ShowStepImmediately(Control activeStep, params Control[] inactiveSteps)
    {
        RestoreVisibleState(activeStep);
        foreach (var inactiveStep in inactiveSteps)
        {
            PrepareHiddenState(inactiveStep, 28);
            inactiveStep.IsHitTestVisible = false;
        }
    }

    public void Cancel()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
        _anchorTargetY = null;
        ResetAnchorTranslation();
        ResetMotionTranslation();
        _isPlannedAnchorMove = false;
        _plannedAnchorLayoutDelta = 0;
    }

    private CancellationToken BeginAnimation()
    {
        Cancel();
        _animationCancellation = new CancellationTokenSource();
        return _animationCancellation.Token;
    }

    private void ApplyNavigationPaneState(bool isVisible, bool animate)
    {
        if (_navigationPane is null)
            return;

        var translation = GetTranslation(_navigationPane);
        var paneTransitions = _navigationPane.Transitions;
        var translationTransitions = translation.Transitions;
        _isNavigationPaneVisible = isVisible;
        if (isVisible)
        {
            _navigationPane.Transitions = null;
            translation.Transitions = null;
            _navigationPane.Width = _navigationPaneWidth;
            if (animate)
            {
                _navigationPane.Transitions = paneTransitions;
                translation.Transitions = translationTransitions;
            }
        }
        else if (!animate)
        {
            _navigationPane.Transitions = null;
            translation.Transitions = null;
            _navigationPane.Width = 0;
        }

        _navigationPane.IsHitTestVisible = isVisible;
        _navigationPane.Opacity = isVisible ? 1 : 0;
        translation.X = isVisible ? 0 : -24;

        if (!animate)
        {
            _navigationPane.Transitions = paneTransitions;
            translation.Transitions = translationTransitions;
        }
    }

    private void CollapseHiddenNavigationPane()
    {
        if (_navigationPane is null || _isNavigationPaneVisible)
            return;

        var paneTransitions = _navigationPane.Transitions;
        _navigationPane.Transitions = null;
        _navigationPane.Width = 0;
        _navigationPane.Transitions = paneTransitions;
    }

    private void ApplyImmediateState(bool showWizard)
    {
        Cancel();
        var overviewTranslation = GetTranslation(_overview);
        var wizardTranslation = GetTranslation(_wizard);
        var overviewTransitions = _overview.Transitions;
        var wizardTransitions = _wizard.Transitions;
        var overviewTranslationTransitions = overviewTranslation.Transitions;
        var wizardTranslationTransitions = wizardTranslation.Transitions;
        _overview.Transitions = null;
        _wizard.Transitions = null;
        overviewTranslation.Transitions = null;
        wizardTranslation.Transitions = null;

        _overview.Opacity = showWizard ? 0 : 1;
        overviewTranslation.X = showWizard ? -28 : 0;
        _overview.IsVisible = !showWizard;
        _overview.IsHitTestVisible = !showWizard;
        _wizard.Opacity = showWizard ? 1 : 0;
        wizardTranslation.X = showWizard ? 0 : 36;
        _wizard.IsVisible = showWizard;
        _wizard.IsHitTestVisible = showWizard;

        if (showWizard)
            InitializeAnchorTarget();

        _overview.Transitions = overviewTransitions;
        _wizard.Transitions = wizardTransitions;
        overviewTranslation.Transitions = overviewTranslationTransitions;
        wizardTranslation.Transitions = wizardTranslationTransitions;
    }

    private void RevealOverviewBehindWizard()
    {
        var translation = GetTranslation(_overview);
        var overviewTransitions = _overview.Transitions;
        var translationTransitions = translation.Transitions;
        _overview.Transitions = null;
        translation.Transitions = null;
        _overview.IsVisible = true;
        _overview.Opacity = 1;
        _overview.IsHitTestVisible = false;
        translation.X = 0;
        _overview.Transitions = overviewTransitions;
        translation.Transitions = translationTransitions;
    }

    private static void PrepareForEntry(Control control, double offset)
    {
        PrepareHiddenState(control, offset);
        control.IsVisible = true;
    }

    private static void PrepareHiddenState(Control control, double offset)
    {
        var translation = GetTranslation(control);
        control.Opacity = 0;
        translation.X = offset;
    }

    private void PrepareOverlayEntryState(double offset)
    {
        var translation = GetTranslation(_wizard);
        var wizardTransitions = _wizard.Transitions;
        var translationTransitions = translation.Transitions;
        _wizard.Transitions = null;
        translation.Transitions = null;
        _wizard.Opacity = 1;
        translation.X = offset;
        _wizard.IsVisible = true;
        _wizard.Transitions = wizardTransitions;
        translation.Transitions = translationTransitions;
    }

    private static async Task RunCompactOverlaySlideAsync(
        TranslateTransform translation,
        double target,
        CancellationToken cancellationToken)
    {
        var transitions = translation.Transitions;
        translation.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = CompactOverlaySlideDuration,
                Easing = new CubicEaseInOut()
            }
        };

        try
        {
            translation.X = target;
            await Task.Delay(MotionDurations.CompactPageSlide, cancellationToken);
        }
        finally
        {
            translation.Transitions = transitions;
        }
    }

    private static void RestoreVisibleState(Control control)
    {
        var translation = GetTranslation(control);
        control.Opacity = 1;
        control.IsHitTestVisible = true;
        translation.X = 0;
    }

    private static async Task RunStepAnimationAsync(
        Control target,
        double fromOpacity,
        double toOpacity,
        double fromOffset,
        double toOffset,
        CancellationToken cancellationToken)
    {
        var translation = GetTranslation(target);
        target.Opacity = toOpacity;
        translation.X = toOffset;

        await Task.WhenAll(
            CreateAnimation(Visual.OpacityProperty, fromOpacity, toOpacity)
                .RunAsync(target, cancellationToken),
            CreateAnimation(TranslateTransform.XProperty, fromOffset, toOffset)
                .RunAsync(target, cancellationToken));
    }

    private static Animation CreateAnimation(
        AvaloniaProperty property,
        object? from,
        object? to)
        => new()
        {
            Duration = PhaseDuration,
            Easing = new CubicEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(property, from) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(property, to) }
                }
            }
        };

    private void OnLayoutContainerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Visual.BoundsProperty ||
            _layoutAnchor is null ||
            !_wizard.IsVisible ||
            !_layoutAnchor.IsVisible)
            return;

        var oldBounds = args.GetOldValue<Rect>();
        var newBounds = args.GetNewValue<Rect>();
        var layoutOffset = oldBounds.Y - newBounds.Y;
        if (Math.Abs(layoutOffset) < 0.5 || _anchorTargetY is null || _suppressAnchorLayoutTracking)
            return;

        var translation = GetTranslation(_layoutAnchor);
        _anchorTargetY += newBounds.Y - oldBounds.Y;
        if (_isPlannedAnchorMove)
        {
            _plannedAnchorLayoutDelta += newBounds.Y - oldBounds.Y;
            translation.Y += layoutOffset;
            return;
        }

        StartAnchorAnimation(translation.Y + layoutOffset);
    }

    private void OnWizardSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (!_wizard.IsVisible || _layoutAnchor is null)
            return;

        // A viewport resize moves the centered wizard as a whole. The reveal icon
        // must follow that layout pass immediately instead of animating from its
        // previous position. Step-height changes are handled by the container
        // bounds listener above and keep their dedicated transition.
        _anchorTargetY = null;
        _isPlannedAnchorMove = false;
        _plannedAnchorLayoutDelta = 0;
        ResetAnchorTranslation();
        ResetMotionTranslation();
        Dispatcher.UIThread.Post(
            InitializeAnchorTarget,
            DispatcherPriority.Loaded);
    }

    private void BeginPlannedAnchorMove(Control outgoingStep, Control incomingStep)
    {
        if (_layoutAnchor is null || _layoutMotionTarget is null || _layoutContainer is null)
            return;

        var incomingHeight = MeasureStepHeight(incomingStep, outgoingStep.Bounds.Width);
        var outgoingHeight = outgoingStep.Bounds.Height;
        if (incomingHeight <= 0 || outgoingHeight <= 0)
            return;

        var predictedLayoutDelta = (outgoingHeight - incomingHeight) / 2;
        if (Math.Abs(predictedLayoutDelta) < 0.5)
            return;

        _isPlannedAnchorMove = true;
        _plannedAnchorLayoutDelta = 0;
        SetTranslationImmediately(GetTranslation(_layoutAnchor), 0);
        SetTranslationImmediately(GetTranslation(_layoutMotionTarget), 0);
        AnimateTranslation(
            GetTranslation(_layoutMotionTarget),
            predictedLayoutDelta / 2,
            new SineEaseIn());
    }

    private double MeasureStepHeight(Control step, double currentStepWidth)
    {
        var wasVisible = step.IsVisible;
        _suppressAnchorLayoutTracking = true;
        try
        {
            step.SetCurrentValue(Visual.IsVisibleProperty, true);
            var availableWidth = currentStepWidth > 0
                ? currentStepWidth
                : Math.Max(0, _layoutContainer?.Bounds.Width ?? 0);
            step.Measure(new Size(availableWidth, double.PositiveInfinity));
            return step.DesiredSize.Height;
        }
        finally
        {
            step.SetCurrentValue(Visual.IsVisibleProperty, wasVisible);
            _suppressAnchorLayoutTracking = false;
        }
    }

    private Task ContinuePlannedAnchorMove(CancellationToken cancellationToken)
    {
        if (!_isPlannedAnchorMove || _layoutMotionTarget is null)
            return Task.CompletedTask;

        AnimateTranslation(
            GetTranslation(_layoutMotionTarget),
            _plannedAnchorLayoutDelta,
            new SineEaseOut());
        return Task.Delay(
            PhaseDuration + TimeSpan.FromMilliseconds(16),
            cancellationToken);
    }

    private void FinishPlannedAnchorMove(CancellationToken cancellationToken)
    {
        if (!_isPlannedAnchorMove ||
            _animationCancellation is null ||
            _animationCancellation.Token != cancellationToken)
            return;

        ResetAnchorTranslation();
        ResetMotionTranslation();
        _isPlannedAnchorMove = false;
        _plannedAnchorLayoutDelta = 0;
    }

    private static void AnimateTranslation(
        TranslateTransform translation,
        double target,
        Easing easing)
    {
        translation.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = PhaseDuration,
                Easing = easing
            }
        };
        translation.Y = target;
    }

    private static void SetTranslationImmediately(TranslateTransform translation, double value)
    {
        translation.Transitions = null;
        translation.Y = value;
    }

    private void InitializeAnchorTarget()
    {
        if (_layoutAnchor is null || !_wizard.IsVisible || !_layoutAnchor.IsVisible)
            return;

        var point = _layoutAnchor.TranslatePoint(default, _wizard);
        if (point is null)
            return;

        _anchorTargetY = point.Value.Y - GetTranslation(_layoutAnchor).Y;
    }

    private void StartAnchorAnimation(double fromOffset)
    {
        if (_layoutAnchor is null)
            return;

        var translation = GetTranslation(_layoutAnchor);
        SetTranslationImmediately(translation, fromOffset);
        translation.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = AnchorMoveDuration,
                Easing = new CubicEaseInOut()
            }
        };
        translation.Y = 0;
    }

    private void ResetAnchorTranslation()
    {
        if (_layoutAnchor?.RenderTransform is TranslateTransform translation)
            SetTranslationImmediately(translation, 0);
    }

    private void ResetMotionTranslation()
    {
        if (_layoutMotionTarget?.RenderTransform is TranslateTransform translation)
            SetTranslationImmediately(translation, 0);
    }

    private static TranslateTransform GetTranslation(Control control)
        => (TranslateTransform)control.RenderTransform!;
}
