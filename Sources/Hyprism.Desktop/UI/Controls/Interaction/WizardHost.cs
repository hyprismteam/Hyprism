// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Labs.Lottie;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hyprism.Desktop.Controls;

/// <summary>
/// Owns the shared lifecycle around a wizard screen, its steps, and reveal animation.
/// </summary>
public sealed class WizardHost
{
    private readonly WizardScreenTransition _transition;
    private readonly Control _overview;
    private readonly Control _wizard;
    private readonly Lottie? _revealAnimation;
    private readonly WizardRevealIcon? _revealIcon;
    private readonly IReadOnlyDictionary<Control, string> _stepAnimationPaths;
    private readonly Control[] _steps;
    private readonly Dictionary<Control, (Control Previous, Action SelectPrevious)> _previousSteps = [];
    private Func<bool>? _isOpen;
    private Action? _close;
    private Func<bool>? _cancelActiveOperation;
    private bool _isStepTransitioning;
    private bool _stepTransitionForward;
    private bool _backRequested;
    private bool _isClosing;
    private Action? _closeCompletion;
    private TopLevel? _topLevel;

    public WizardHost(
        Control overview,
        Control wizard,
        Control? navigationPane = null,
        Control? layoutAnchor = null,
        Control? layoutMotionTarget = null,
        Lottie? revealAnimation = null,
        params Control[] steps)
    {
        _overview = overview;
        _wizard = wizard;
        _transition = new WizardScreenTransition(
            overview,
            wizard,
            navigationPane,
            layoutAnchor,
            layoutMotionTarget);
        _revealAnimation = revealAnimation;
        _stepAnimationPaths = new Dictionary<Control, string>();
        _steps = steps;
        wizard.Focusable = true;
        wizard.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        wizard.AttachedToVisualTree += OnWizardAttached;
        wizard.DetachedFromVisualTree += OnWizardDetached;
    }

    public WizardHost(
        Control overview,
        Control wizard,
        Control? navigationPane,
        WizardRevealIcon revealIcon,
        params WizardStepDefinition[] steps)
        : this(
            overview,
            wizard,
            navigationPane,
            revealIcon.Anchor,
            revealIcon.MotionTarget,
            revealIcon.Animation,
            steps.Select(step => step.Content).ToArray())
    {
        _revealIcon = revealIcon;
        _stepAnimationPaths = steps.ToDictionary(
            step => step.Content,
            step => step.AnimationPath);
    }

    public void ConfigureNavigation(
        Func<bool> isOpen,
        Action close,
        Func<bool>? cancelActiveOperation = null)
    {
        _isOpen = isOpen;
        _close = close;
        _cancelActiveOperation = cancelActiveOperation;
    }

    public void RegisterPreviousStep(Control step, Control previous, Action selectPrevious)
        => _previousSteps.Add(step, (previous, selectPrevious));

    public bool TryNavigateBack()
    {
        if (_isOpen?.Invoke() != true)
            return false;

        var focused = TopLevel.GetTopLevel(_wizard)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox input && _wizard.IsVisualAncestorOf(input))
        {
            _wizard.Focus();
        }
        else if (_cancelActiveOperation?.Invoke() == true)
        {
            // The active operation handles this Escape before step navigation.
        }
        else if (_isStepTransitioning)
        {
            _backRequested = _stepTransitionForward;
        }
        else if (_steps.FirstOrDefault(step => step.IsVisible) is { } activeStep &&
                 _previousSteps.ContainsKey(activeStep))
        {
            _ = NavigateBackAsync();
        }
        else
        {
            _close?.Invoke();
        }

        return true;
    }

    public Task NavigateBackAsync()
    {
        if (_isStepTransitioning)
        {
            _backRequested = _stepTransitionForward;
            return Task.CompletedTask;
        }

        var activeStep = _steps.FirstOrDefault(step => step.IsVisible);
        if (activeStep is null || !_previousSteps.TryGetValue(activeStep, out var previous) ||
            _isOpen is null)
            return Task.CompletedTask;

        return SwitchStepAsync(
            activeStep,
            previous.Previous,
            forward: false,
            previous.SelectPrevious,
            _isOpen);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (!args.Handled && args.Key == Key.Escape && TryNavigateBack())
            args.Handled = true;
    }

    private void OnWizardAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _topLevel = TopLevel.GetTopLevel(_wizard);
        _topLevel?.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Bubble);
    }

    private void OnWizardDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _topLevel?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);
        _topLevel = null;
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs args)
    {
        if (!args.Handled && args.Key == Key.Escape &&
            (_wizard.IsEffectivelyVisible || _overview.IsEffectivelyVisible) &&
            TryNavigateBack())
            args.Handled = true;
    }

    public Task OpenAsync(Func<bool> shouldRemainOpen, Action? onOpened = null)
    {
        _isClosing = false;
        _closeCompletion = null;
        NormalizeSteps();
        SelectActiveStepAnimation();
        return _transition.OpenAsync(
            shouldRemainOpen,
            () =>
            {
                RestartReveal();
                _wizard.Focus();
                onOpened?.Invoke();
            });
    }

    public Task OpenCompactOverlayAsync(
        Func<bool> shouldRemainOpen,
        double horizontalOffset,
        Action? onOpened = null)
    {
        _isClosing = false;
        _closeCompletion = null;
        NormalizeSteps();
        SelectActiveStepAnimation();
        return _transition.OpenCompactOverlayAsync(
            shouldRemainOpen,
            RestartReveal,
            () =>
            {
                _wizard.Focus();
                onOpened?.Invoke();
            },
            horizontalOffset);
    }

    public Task CloseAsync(Func<bool> shouldRemainClosed, Action? onClosed = null)
    {
        _isClosing = true;
        _closeCompletion = onClosed;
        return _transition.CloseAsync(shouldRemainClosed, CompleteClose);
    }

    public Task CloseCompactOverlayAsync(
        Func<bool> shouldRemainClosed,
        double horizontalOffset,
        Action? onClosed = null)
    {
        _isClosing = true;
        _closeCompletion = onClosed;
        return _transition.CloseCompactOverlayAsync(
            shouldRemainClosed,
            CompleteClose,
            horizontalOffset);
    }

    public void SyncLayout(bool isOpen)
    {
        if (isOpen)
        {
            _isClosing = false;
            _closeCompletion = null;
            ShowWizardImmediately();
        }
        else
        {
            ShowOverviewImmediately();
            if (_isClosing)
                CompleteClose();
        }
    }

    private void CompleteClose()
    {
        if (!_isClosing)
            return;

        _isClosing = false;
        var completion = _closeCompletion;
        _closeCompletion = null;
        completion?.Invoke();
        NormalizeSteps();
    }

    public async Task SwitchStepAsync(
        Control outgoingStep,
        Control incomingStep,
        bool forward,
        Action switchStep,
        Func<bool> shouldRemainOpen)
    {
        if (_isStepTransitioning)
        {
            if (!forward)
                _backRequested = _stepTransitionForward;
            return;
        }

        _isStepTransitioning = true;
        _stepTransitionForward = forward;
        try
        {
            var completed = await _transition.SwitchStepAsync(
                outgoingStep,
                incomingStep,
                forward,
                switchStep,
                shouldRemainOpen);
            if (completed && shouldRemainOpen())
            {
                if (!forward && ReferenceEquals(incomingStep, _steps.FirstOrDefault()))
                    ShowStepAnimationFinalFrame(incomingStep);
                else
                    PlayStepAnimation(incomingStep);
            }
        }
        finally
        {
            _isStepTransitioning = false;
            var navigateBack = _backRequested && forward && shouldRemainOpen();
            _backRequested = false;
            if (navigateBack)
                await NavigateBackAsync();
        }
    }

    public async Task ShowWizardForCompactEntryAsync()
    {
        NormalizeSteps();
        SelectActiveStepAnimation();
        _transition.ShowWizardImmediately();
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        _wizard.Focus();
        RestartReveal();
    }

    public void ShowOverviewImmediately(Action? onClosed = null)
    {
        _transition.ShowOverviewImmediately();
        onClosed?.Invoke();
        NormalizeSteps();
    }

    public void ShowWizardImmediately()
    {
        NormalizeSteps();
        SelectActiveStepAnimation();
        _transition.ShowWizardImmediately();
    }

    public void ShowNavigationPane(bool animate)
        => _transition.ShowNavigationPane(animate);

    public void HideNavigationPane(bool animate)
        => _transition.HideNavigationPane(animate);

    public void ResetNavigationPane()
        => _transition.ResetNavigationPane();

    public void Cancel()
        => _transition.Cancel();

    public void RestartReveal()
    {
        var activeStep = _steps.FirstOrDefault(step => step.IsVisible);
        if (activeStep is not null && PlayStepAnimation(activeStep))
            return;

        if (_revealIcon is not null &&
            _revealIcon.AnimationPath is { Length: > 0 } animationPath)
        {
            _revealIcon.Play(animationPath);
            return;
        }

        if (_revealAnimation is null)
            return;

        _revealAnimation.SeekToProgress(0);
        _revealAnimation.Start();
    }

    private bool PlayStepAnimation(Control step)
    {
        if (_revealIcon is null || !_stepAnimationPaths.TryGetValue(step, out var animationPath))
            return false;

        _revealIcon.Play(animationPath);
        return true;
    }

    private void ShowStepAnimationFinalFrame(Control step)
    {
        if (_revealIcon is not null &&
            _stepAnimationPaths.TryGetValue(step, out var animationPath))
        {
            _revealIcon.ShowFinalFrame(animationPath);
        }
    }

    private void SelectActiveStepAnimation()
    {
        if (_revealIcon is null)
            return;

        var activeStep = _steps.FirstOrDefault(step => step.IsVisible);
        if (activeStep is not null &&
            _stepAnimationPaths.TryGetValue(activeStep, out var animationPath))
        {
            _revealIcon.ShowInitialFrame(animationPath);
        }
    }

    private void NormalizeSteps()
    {
        if (_steps.Length == 0)
            return;

        var activeStep = _steps.FirstOrDefault(step => step.IsVisible) ?? _steps[0];
        _transition.ShowStepImmediately(
            activeStep,
            _steps.Where(step => !ReferenceEquals(step, activeStep)).ToArray());
    }
}

public sealed record WizardStepDefinition(Control Content, string AnimationPath);
