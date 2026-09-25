// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsView : UserControl
{
    private readonly WizardHost _downloadSourceWizard;
    private readonly AdaptiveMasterDetailHost _layoutHost;
    private INotifyPropertyChanged? _viewModel;
    private bool _isDownloadSourceWizardVisible;

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
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
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

    private void OnSettingsViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
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

    private async void OnBeginAutomaticSourceAdditionClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        await _downloadSourceWizard.SwitchStepAsync(
            SourceAdditionChoiceContent,
            AutomaticSourceAdditionContent,
            forward: true,
            () => viewModel.BeginAutomaticMirrorAdditionCommand.Execute(null),
            () => viewModel.IsAddingMirror);
    }

    private async void OnBeginManualSourceAdditionClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        await _downloadSourceWizard.SwitchStepAsync(
            SourceAdditionChoiceContent,
            ManualSourceAdditionContent,
            forward: true,
            () => viewModel.BeginManualMirrorAdditionCommand.Execute(null),
            () => viewModel.IsAddingMirror);
    }

    private async void OnReturnToSourceAdditionChoiceClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        var outgoingStep = viewModel.IsManualSourceVisible
            ? ManualSourceAdditionContent
            : AutomaticSourceAdditionContent;
        await _downloadSourceWizard.SwitchStepAsync(
            outgoingStep,
            SourceAdditionChoiceContent,
            forward: false,
            () => viewModel.ReturnToMirrorAdditionChoiceCommand.Execute(null),
            () => viewModel.IsAddingMirror);
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
            viewModel.CancelAddMirrorCommand.Execute(null);
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
