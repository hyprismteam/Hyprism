// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceCreatorView : UserControl
{
    private static readonly TimeSpan VersionLoadingFadeDuration = MotionDurations.VersionLoadingFade;
    private INotifyPropertyChanged? _viewModel;
    private CancellationTokenSource? _versionLoadingCancellation;

    public InstanceCreatorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public WizardRevealIcon Reveal => InstanceWizardReveal;
    public Func<InstanceWizardStage, InstanceWizardStage, bool, Action, Task>? NavigateStageAsync { get; set; }

    public Control StepControl(InstanceWizardStage stage) => stage switch
    {
        InstanceWizardStage.Choice => InstanceChoiceContent,
        InstanceWizardStage.Download => InstanceDownloadContent,
        InstanceWizardStage.Import => InstanceImportContent,
        InstanceWizardStage.ExportKind => InstanceExportKindContent,
        InstanceWizardStage.ExportFormat => InstanceExportFormatContent,
        _ => InstanceChoiceContent
    };

    private async Task NavigateAsync(InstanceWizardStage destination, bool forward, Action updateStage)
    {
        if (DataContext is not InstancesViewModel viewModel ||
            viewModel.InstanceWizardStage == destination)
            return;

        if (NavigateStageAsync is { } navigate)
            await navigate(viewModel.InstanceWizardStage, destination, forward, updateStage);
        else
            updateStage();
    }

    private async void OnChooseInstanceDownloadClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            await NavigateAsync(InstanceWizardStage.Download, true,
                () => viewModel.ChooseInstanceDownloadCommand.Execute(null));
    }

    private async void OnChooseInstanceImportClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            await NavigateAsync(InstanceWizardStage.Import, true,
                () => viewModel.ChooseInstanceImportCommand.Execute(null));
    }

    private async void OnSelectExportKindClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel && sender is Button button)
            await NavigateAsync(InstanceWizardStage.ExportFormat, true,
                () => viewModel.SelectExportKindCommand.Execute(button.CommandParameter));
    }

    private async void OnBackInstanceWizardClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not InstancesViewModel viewModel)
            return;
        if (viewModel.TryCancelInstanceImport())
            return;

        var previous = viewModel.InstanceWizardStage switch
        {
            InstanceWizardStage.Download or InstanceWizardStage.Import
                when viewModel.CanReturnToInstanceChoice => InstanceWizardStage.Choice,
            InstanceWizardStage.ExportFormat => InstanceWizardStage.ExportKind,
            _ => viewModel.InstanceWizardStage
        };
        if (previous == viewModel.InstanceWizardStage)
            viewModel.BackInstanceWizardCommand.Execute(null);
        else
            await NavigateAsync(previous, false,
                () => viewModel.BackInstanceWizardCommand.Execute(null));
    }

    private void OnImportActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.ArmInstanceImportCancellation();
    }

    public void RefreshBranchIndicator()
        => UpdateBranchIndicator(animate: false);

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateBranchIndicator(animate: false);
        ApplyVersionLoadingStateImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstancesViewModel.NewInstanceBranch) &&
            DataContext is InstancesViewModel { IsInstanceCreatorOpen: true })
        {
            UpdateBranchIndicator(animate: true);
        }

        if (args.PropertyName is not nameof(InstancesViewModel.IsInstanceVersionsLoading))
            return;

        if (DataContext is InstancesViewModel { IsInstanceVersionsLoading: true })
            ShowVersionLoading();
        else
            _ = HideVersionLoadingAsync();
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
}
