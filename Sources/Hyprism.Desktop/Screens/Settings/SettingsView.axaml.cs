// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsView : UserControl
{
    private readonly WizardHost _downloadSourceWizard;
    private readonly AdaptiveMasterDetailHost _layoutHost;
    private INotifyPropertyChanged? _viewModel;
    private bool _isDownloadSourceWizardVisible;
    private bool _isSourceStepTransitioning;
    private bool _isSourceStepTransitionForward;
    private bool _returnAfterSourceStepTransition;
    private MirrorSourceViewModel? _activeMirrorMenu;
    private Border? _activeMirrorMenuTarget;
    private CancellationTokenSource? _mirrorMenuCloseCancellation;
    private TopLevel? _mirrorMenuTopLevel;
    private Window? _mirrorMenuWindow;

    public SettingsView()
    {
        InitializeComponent();
        _downloadSourceWizard = new WizardHost(
            SettingsOverview,
            DownloadSourceWizardScreen,
            SettingsCategoryRail,
            DownloadSourceWizardReveal.Anchor,
            DownloadSourceWizardReveal.MotionTarget,
            DownloadSourceWizardReveal.Animation,
            SourceAdditionChoiceContent,
            AutomaticSourceAdditionContent,
            ManualSourceAdditionContent);
        _layoutHost = new AdaptiveMasterDetailHost(
            SettingsLayout,
            SettingsCategoryRail,
            SettingsMain,
            CompactSettingsToolbar,
            SettingsContentHost,
            compact => SettingsCategoryRail.Classes.Set("compact", compact));
        DownloadsView.MirrorMenuToggled += OnMirrorMenuToggled;
        SettingsContent.ScrollChanged += (_, _) => CloseMirrorMenu();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _mirrorMenuTopLevel = TopLevel.GetTopLevel(this);
        _mirrorMenuTopLevel?.AddHandler(
            PointerPressedEvent,
            OnMirrorMenuPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _mirrorMenuTopLevel?.AddHandler(
            KeyDownEvent,
            OnMirrorMenuKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _mirrorMenuTopLevel?.AddHandler(
            PointerPressedEvent,
            OnWizardPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _mirrorMenuTopLevel?.AddHandler(
            KeyDownEvent,
            OnWizardKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _mirrorMenuWindow = _mirrorMenuTopLevel as Window;
        if (_mirrorMenuWindow is not null)
            _mirrorMenuWindow.Deactivated += OnMirrorMenuWindowDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _mirrorMenuTopLevel?.RemoveHandler(PointerPressedEvent, OnMirrorMenuPointerPressed);
        _mirrorMenuTopLevel?.RemoveHandler(KeyDownEvent, OnMirrorMenuKeyDown);
        _mirrorMenuTopLevel?.RemoveHandler(PointerPressedEvent, OnWizardPointerPressed);
        _mirrorMenuTopLevel?.RemoveHandler(KeyDownEvent, OnWizardKeyDown);
        if (_mirrorMenuWindow is not null)
            _mirrorMenuWindow.Deactivated -= OnMirrorMenuWindowDeactivated;
        _mirrorMenuTopLevel = null;
        _mirrorMenuWindow = null;
        ResetMirrorMenu();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        ResetMirrorMenu();
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _isDownloadSourceWizardVisible = DataContext is SettingsViewModel { IsAddingMirror: true };
        if (_isDownloadSourceWizardVisible)
            _ = PlayDownloadSourceWizardOpenAsync();
        else
            HideDownloadSourceWizardImmediately();

    }

    private void OnMirrorDeleteModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.CompleteMirrorDeletionClose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.IsDownloads) &&
            DataContext is SettingsViewModel { IsDownloads: false })
        {
            CloseMirrorMenu();
        }

        if (args.PropertyName != nameof(SettingsViewModel.IsAddingMirror))
            return;

        var isVisible = DataContext is SettingsViewModel { IsAddingMirror: true };
        if (_isDownloadSourceWizardVisible == isVisible)
            return;

        _isDownloadSourceWizardVisible = isVisible;
        if (isVisible)
            _ = PlayDownloadSourceWizardOpenAsync();
        else
            _ = PlayDownloadSourceWizardCloseAsync();
    }

    private void OnMirrorMenuToggled(Border target, MirrorSourceViewModel mirror)
    {
        if (ReferenceEquals(_activeMirrorMenu, mirror))
        {
            CloseMirrorMenu();
            return;
        }

        _mirrorMenuCloseCancellation?.Cancel();
        if (_activeMirrorMenu is not null)
            _activeMirrorMenu.IsMenuOpen = false;

        _activeMirrorMenu = mirror;
        _activeMirrorMenuTarget = target;
        mirror.IsMenuOpen = true;
        MirrorActionFlyout.DataContext = mirror;
        MirrorActionFlyout.Opacity = 0;
        MirrorActionFlyout.IsHitTestVisible = true;
        MirrorActionFlyout.IsVisible = true;
        MirrorActionFlyout.Measure(SettingsOverlayRoot.Bounds.Size);

        var targetOrigin = target.TranslatePoint(default, SettingsOverlayRoot);
        if (targetOrigin is null)
        {
            ResetMirrorMenu();
            return;
        }

        var menuSize = MirrorActionFlyout.DesiredSize;
        var viewport = SettingsOverlayRoot.Bounds.Size;
        var left = Math.Clamp(targetOrigin.Value.X + target.Bounds.Width - menuSize.Width,
            0, Math.Max(0, viewport.Width - menuSize.Width));
        var below = targetOrigin.Value.Y + target.Bounds.Height + 8;
        var top = below + menuSize.Height <= viewport.Height
            ? below
            : Math.Max(0, targetOrigin.Value.Y - menuSize.Height - 8);
        Canvas.SetLeft(MirrorActionFlyout, left);
        Canvas.SetTop(MirrorActionFlyout, top);
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_activeMirrorMenu, mirror))
                MirrorActionFlyout.Opacity = 1;
        }, DispatcherPriority.Loaded);
    }

    private void OnMirrorMenuPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (_activeMirrorMenu is null || args.Source is not Visual source)
            return;

        if (ReferenceEquals(source, MirrorActionFlyout) ||
            MirrorActionFlyout.IsVisualAncestorOf(source) ||
            _activeMirrorMenuTarget is { } target &&
            (ReferenceEquals(source, target) || target.IsVisualAncestorOf(source)))
        {
            return;
        }

        CloseMirrorMenu();
    }

    private void OnMirrorMenuKeyDown(object? sender, KeyEventArgs args)
    {
        if (_activeMirrorMenu is null || args.Key != Key.Escape)
            return;

        CloseMirrorMenu();
        args.Handled = true;
    }

    private void OnWizardPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is not SettingsViewModel { IsAddingMirror: true } ||
            GetFocusedMirrorInput() is not { } input ||
            args.Source is not Visual source ||
            ReferenceEquals(source, input) || input.IsVisualAncestorOf(source))
            return;

        DownloadSourceWizardScreen.Focus();
    }

    private void OnWizardKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Escape ||
            DataContext is not SettingsViewModel { IsAddingMirror: true } viewModel)
            return;

        if (GetFocusedMirrorInput() is not null)
        {
            DownloadSourceWizardScreen.Focus();
        }
        else if (_isSourceStepTransitioning)
        {
            _returnAfterSourceStepTransition = _isSourceStepTransitionForward;
        }
        else if (viewModel.IsAddSourceChoiceVisible)
        {
            viewModel.CancelAddMirrorCommand.Execute(null);
        }
        else if (viewModel.IsMirrorOperationBusy)
        {
            viewModel.CancelActiveMirrorAddition();
        }
        else
        {
            _ = ReturnToSourceAdditionChoiceAsync();
        }

        args.Handled = true;
    }

    private TextBox? GetFocusedMirrorInput()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (ReferenceEquals(focused, MirrorUrlTextBox) && MirrorUrlTextBox.IsVisible)
            return MirrorUrlTextBox;
        if (ReferenceEquals(focused, ManualMirrorJsonTextBox) && ManualMirrorJsonTextBox.IsVisible)
            return ManualMirrorJsonTextBox;
        return null;
    }

    private void OnMirrorMenuWindowDeactivated(object? sender, EventArgs args)
        => CloseMirrorMenu();

    private void OnRemoveMirrorClicked(object? sender, RoutedEventArgs args)
        => CloseMirrorMenu();

    private void CloseMirrorMenu()
    {
        if (_activeMirrorMenu is null)
            return;

        _activeMirrorMenu.IsMenuOpen = false;
        _activeMirrorMenu = null;
        _activeMirrorMenuTarget = null;
        MirrorActionFlyout.Opacity = 0;
        MirrorActionFlyout.IsHitTestVisible = false;
        _mirrorMenuCloseCancellation?.Cancel();
        _mirrorMenuCloseCancellation = new CancellationTokenSource();
        _ = HideMirrorMenuAfterFadeAsync(_mirrorMenuCloseCancellation);
    }

    private async Task HideMirrorMenuAfterFadeAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(MotionDurations.PopupCloseRetention, cancellation.Token);
            if (_activeMirrorMenu is null)
            {
                MirrorActionFlyout.IsVisible = false;
                MirrorActionFlyout.DataContext = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Reopening the menu keeps the same flyout visible.
        }
        finally
        {
            if (ReferenceEquals(_mirrorMenuCloseCancellation, cancellation))
                _mirrorMenuCloseCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ResetMirrorMenu()
    {
        _mirrorMenuCloseCancellation?.Cancel();
        _activeMirrorMenu?.IsMenuOpen = false;
        _activeMirrorMenu = null;
        _activeMirrorMenuTarget = null;
        MirrorActionFlyout.IsVisible = false;
        MirrorActionFlyout.Opacity = 0;
        MirrorActionFlyout.IsHitTestVisible = false;
        MirrorActionFlyout.DataContext = null;
    }

    private void OnSettingsViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        CloseMirrorMenu();
        _layoutHost.Update(e.NewSize.Width, hasMaster: true);
        var viewModel = DataContext as SettingsViewModel;
        if (viewModel is not null)
            viewModel.IsCompactLayout = _layoutHost.IsCompact;

        if (_layoutHost.IsCompact)
        {
            _downloadSourceWizard.ResetNavigationPane();
            return;
        }

        if (viewModel?.IsAddingMirror == true)
            _downloadSourceWizard.HideNavigationPane(animate: false);
        else
            _downloadSourceWizard.ShowNavigationPane(animate: false);
    }

    private void OnSettingsCategoryClicked(object? sender, RoutedEventArgs e)
    {
        CloseMirrorMenu();
        _layoutHost.RememberDetail();
        Dispatcher.UIThread.Post(SettingsContent.ScrollToHome, DispatcherPriority.Background);
        if (!_layoutHost.IsCompact)
            return;

        _layoutHost.OpenDetail();
    }

    private void OnCompactSettingsBackClicked(object? sender, RoutedEventArgs e)
        => TryCloseCompactContent();

    private void OnAuthServerAddPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.ArmAuthServerCancellation();
    }

    private void OnMirrorAddPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.ArmMirrorAdditionCancellation();
    }

    private async void OnBeginAutomaticSourceAdditionClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        await SwitchSourceStepAsync(
            SourceAdditionChoiceContent,
            AutomaticSourceAdditionContent,
            forward: true,
            () => viewModel.BeginAutomaticMirrorAdditionCommand.Execute(null),
            viewModel);
    }

    private async void OnBeginManualSourceAdditionClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        await SwitchSourceStepAsync(
            SourceAdditionChoiceContent,
            ManualSourceAdditionContent,
            forward: true,
            () => viewModel.BeginManualMirrorAdditionCommand.Execute(null),
            viewModel);
    }

    private async void OnReturnToSourceAdditionChoiceClicked(object? sender, RoutedEventArgs args)
        => await ReturnToSourceAdditionChoiceAsync();

    private async Task ReturnToSourceAdditionChoiceAsync()
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        var outgoingStep = viewModel.IsManualSourceVisible
            ? ManualSourceAdditionContent
            : AutomaticSourceAdditionContent;
        await SwitchSourceStepAsync(
            outgoingStep,
            SourceAdditionChoiceContent,
            forward: false,
            () => viewModel.ReturnToMirrorAdditionChoiceCommand.Execute(null),
            viewModel);
    }

    private async Task SwitchSourceStepAsync(
        Control outgoingStep,
        Control incomingStep,
        bool forward,
        Action switchStep,
        SettingsViewModel viewModel)
    {
        if (_isSourceStepTransitioning)
        {
            if (!forward)
                _returnAfterSourceStepTransition = _isSourceStepTransitionForward;
            return;
        }

        _isSourceStepTransitioning = true;
        _isSourceStepTransitionForward = forward;
        try
        {
            await _downloadSourceWizard.SwitchStepAsync(
                outgoingStep,
                incomingStep,
                forward,
                switchStep,
                () => viewModel.IsAddingMirror);
        }
        finally
        {
            _isSourceStepTransitioning = false;
            var returnToChoice = _returnAfterSourceStepTransition &&
                                 viewModel.IsAddingMirror && !viewModel.IsAddSourceChoiceVisible;
            _returnAfterSourceStepTransition = false;
            if (returnToChoice)
                await ReturnToSourceAdditionChoiceAsync();
        }
    }

    public bool TryCloseCompactContent()
    {
        if (DataContext is SettingsViewModel { IsAddingJavaArgument: true } javaViewModel)
        {
            javaViewModel.CancelAddJavaArgumentCommand.Execute(null);
            return true;
        }

        if (DataContext is SettingsViewModel { IsAddingEnvironmentVariable: true } variableViewModel)
        {
            variableViewModel.CancelAddEnvironmentVariableCommand.Execute(null);
            return true;
        }

        if (DataContext is SettingsViewModel { IsAddingAuthServer: true } authServerViewModel)
        {
            authServerViewModel.CancelAddAuthServerCommand.Execute(null);
            return true;
        }

        if (DataContext is SettingsViewModel { IsAddingMirror: true } viewModel)
        {
            if (GetFocusedMirrorInput() is not null)
                DownloadSourceWizardScreen.Focus();
            else if (_isSourceStepTransitioning)
                _returnAfterSourceStepTransition = _isSourceStepTransitionForward;
            else if (viewModel.IsAddSourceChoiceVisible)
                viewModel.CancelAddMirrorCommand.Execute(null);
            else if (viewModel.IsMirrorOperationBusy)
                viewModel.CancelActiveMirrorAddition();
            else
                _ = ReturnToSourceAdditionChoiceAsync();
            return true;
        }

        return _layoutHost.TryCloseDetail();
    }

    private async Task PlayDownloadSourceWizardOpenAsync()
    {
        if (_layoutHost.IsCompact)
        {
            await _downloadSourceWizard.OpenCompactOverlayAsync(
                () => DataContext is SettingsViewModel { IsAddingMirror: true },
                GetDownloadSourceWizardHorizontalOffset());
            return;
        }

        _downloadSourceWizard.HideNavigationPane(animate: true);
        await _downloadSourceWizard.OpenAsync(
            () => DataContext is SettingsViewModel { IsAddingMirror: true });
    }

    private async Task PlayDownloadSourceWizardCloseAsync()
    {
        if (_layoutHost.IsCompact)
        {
            await _downloadSourceWizard.CloseCompactOverlayAsync(
                () => DataContext is SettingsViewModel { IsAddingMirror: false },
                GetDownloadSourceWizardHorizontalOffset(),
                () =>
                {
                    if (DataContext is SettingsViewModel viewModel)
                        viewModel.CompleteMirrorAdditionTransition();
                });
            return;
        }

        await _downloadSourceWizard.CloseAsync(
            () => DataContext is SettingsViewModel { IsAddingMirror: false },
            () =>
            {
                if (!_layoutHost.IsCompact)
                    _downloadSourceWizard.ShowNavigationPane(animate: true);

                if (DataContext is SettingsViewModel viewModel)
                    viewModel.CompleteMirrorAdditionTransition();
            });
    }

    private double GetDownloadSourceWizardHorizontalOffset()
        => _layoutHost.IsCompact
            ? Math.Max(28, SettingsMain.Bounds.Width)
            : 28;

    private void HideDownloadSourceWizardImmediately()
    {
        _downloadSourceWizard.ShowOverviewImmediately(() =>
        {
            if (DataContext is SettingsViewModel viewModel)
                viewModel.CompleteMirrorAdditionTransition();
        });
        if (!_layoutHost.IsCompact)
            _downloadSourceWizard.ShowNavigationPane(animate: false);
    }
}
