// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Controls;
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
