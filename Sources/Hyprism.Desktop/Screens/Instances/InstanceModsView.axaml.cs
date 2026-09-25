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
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceModsView : UserControl
{
    public const double ModCatalogContentMaxWidth = 820;

    private static readonly TimeSpan ModCatalogSearchFadeDuration = MotionDurations.ContentFade;
    private readonly WizardScreenTransition _installTransition;
    private INotifyPropertyChanged? _viewModel;
    private CancellationTokenSource? _modCatalogLoadingCancellation;
    private bool _modDropActive;

    public InstanceModsView()
    {
        InitializeComponent();
        _installTransition = new WizardScreenTransition(
            ModCatalogBrowseContent,
            ModCatalogInstallScreen);
        DragDrop.SetAllowDrop(ModsDropZone, true);
        ModsDropZone.AddHandler(DragDrop.DragEnterEvent, OnModFilesDragEntered);
        ModsDropZone.AddHandler(DragDrop.DragLeaveEvent, OnModFilesDragLeft);
        ModsDropZone.AddHandler(DragDrop.DropEvent, OnModFilesDropped);
        DataContextChanged += OnDataContextChanged;
    }

    public void SetMaximumWidth(double installedModsWidth, double catalogWidth)
    {
        InstalledModsSection.MaxWidth = installedModsWidth;
        ModCatalogSection.MaxWidth = catalogWidth;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyModCatalogLoadingStateImmediately();
        ApplyModCatalogInstallStateImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstancesViewModel.IsModCatalogLoading))
        {
            if (DataContext is InstancesViewModel { IsModCatalogLoading: true })
                ShowModCatalogLoading();
            else
                _ = HideModCatalogLoadingAsync();
        }

        if (args.PropertyName is nameof(InstancesViewModel.IsInstallingSelectedCatalogMods))
        {
            if (DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: true })
                _ = PlayModCatalogInstallOpenAnimationAsync();
            else
                _ = PlayModCatalogInstallCloseAnimationAsync();
        }
    }

    private void ApplyModCatalogInstallStateImmediately()
    {
        if (DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: true })
            _installTransition.ShowWizardImmediately();
        else
            _installTransition.ShowOverviewImmediately();
    }

    private Task PlayModCatalogInstallOpenAnimationAsync()
        => _installTransition.OpenAsync(
            () => DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: true });

    private Task PlayModCatalogInstallCloseAnimationAsync()
        => _installTransition.CloseAsync(
            () => DataContext is InstancesViewModel { IsInstallingSelectedCatalogMods: false },
            () =>
            {
                if (DataContext is InstancesViewModel viewModel)
                    viewModel.CompleteModCatalogInstallation();
            });

    private void ApplyModCatalogLoadingStateImmediately()
    {
        CancelModCatalogLoadingAnimation();
        var isLoading = DataContext is InstancesViewModel { IsModCatalogLoading: true };
        var listTransitions = ModCatalogList.Transitions;
        var spinnerTransitions = ModCatalogSearchSpinner.Transitions;
        ModCatalogList.Transitions = null;
        ModCatalogSearchSpinner.Transitions = null;
        ModCatalogList.Opacity = isLoading ? 0 : 1;
        ModCatalogList.IsHitTestVisible = !isLoading;
        ModCatalogSearchSpinner.IsVisible = isLoading;
        ModCatalogSearchSpinner.Opacity = isLoading ? 1 : 0;
        ModCatalogList.Transitions = listTransitions;
        ModCatalogSearchSpinner.Transitions = spinnerTransitions;
    }

    private void ShowModCatalogLoading()
    {
        CancelModCatalogLoadingAnimation();
        ModCatalogList.IsHitTestVisible = false;
        ModCatalogList.Opacity = 0;
        ModCatalogSearchSpinner.IsVisible = true;
        ModCatalogSearchSpinner.Opacity = 0;
        _modCatalogLoadingCancellation = new CancellationTokenSource();
        _ = FadeInModCatalogSpinnerAsync(_modCatalogLoadingCancellation.Token);
    }

    private async Task FadeInModCatalogSpinnerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ModCatalogSearchFadeDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not InstancesViewModel { IsModCatalogLoading: true })
            {
                return;
            }

            ModCatalogSearchSpinner.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A completed search or a new search replaces the pending reveal.
        }
    }

    private async Task HideModCatalogLoadingAsync()
    {
        CancelModCatalogLoadingAnimation();
        _modCatalogLoadingCancellation = new CancellationTokenSource();
        var cancellationToken = _modCatalogLoadingCancellation.Token;
        ModCatalogSearchSpinner.Opacity = 0;

        try
        {
            await Task.Delay(ModCatalogSearchFadeDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is InstancesViewModel { IsModCatalogLoading: true })
            {
                return;
            }

            ModCatalogSearchSpinner.IsVisible = false;
            ModCatalogList.IsHitTestVisible = true;
            ModCatalogList.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A new search replaces the pending hide.
        }
    }

    private void CancelModCatalogLoadingAnimation()
    {
        _modCatalogLoadingCancellation?.Cancel();
        _modCatalogLoadingCancellation?.Dispose();
        _modCatalogLoadingCancellation = null;
    }

    private static bool IsModArchiveName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName) &&
           (fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase));

    private void OnModFilesDragEntered(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        if (DataContext is not InstancesViewModel { IsManagedInstanceInstalled: true } ||
            !ContainsFiles(args.DataTransfer))
        {
            args.DragEffects = DragDropEffects.None;
            ShowModDropOverlay(false);
            return;
        }

        args.DragEffects = DragDropEffects.Copy;
        ShowModDropOverlay(true);
    }

    private void OnModFilesDragLeft(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        ShowModDropOverlay(false);
    }

    private async void OnModFilesDropped(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        ShowModDropOverlay(false);
        if (DataContext is not InstancesViewModel viewModel ||
            args.DataTransfer is not IAsyncDataTransfer data ||
            !ContainsFiles(args.DataTransfer))
        {
            return;
        }

        try
        {
            var files = await data.TryGetFilesAsync() ?? [];
            var paths = files
                .Select(file => file.TryGetLocalPath())
                .OfType<string>()
                .Where(IsModArchiveName)
                .ToList();
            await viewModel.ImportModFilesAsync(paths);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed drop keeps the mod list untouched.
        }
    }

    private static bool ContainsFiles(IDataTransfer data)
        => data.Formats is { } formats && formats.Contains(DataFormat.File);

    private void ShowModDropOverlay(bool visible)
    {
        if (_modDropActive == visible)
            return;

        _modDropActive = visible;
        ModDropOverlay.IsVisible = visible;
    }

    private void OnCatalogSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Return) ||
            DataContext is not InstancesViewModel viewModel)
        {
            return;
        }

        args.Handled = true;
        viewModel.SearchModCatalogCommand.Execute(null);
    }

    private void OnCatalogModPreviewRequested(object? sender, TappedEventArgs args)
    {
        if (sender is not Border { DataContext: ModCatalogItemViewModel item } ||
            DataContext is not InstancesViewModel viewModel)
        {
            return;
        }

        if (args.Source is Button or CheckBox ||
            args.Source is Visual visual &&
            (visual.FindAncestorOfType<Button>() is not null ||
             visual.FindAncestorOfType<CheckBox>() is not null))
        {
            return;
        }

        viewModel.SelectModCatalogPreviewCommand.Execute(item);
    }

    private void OnModCatalogScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.Y >= 0 ||
            sender is not ScrollViewer scrollViewer ||
            DataContext is not InstancesViewModel viewModel ||
            !viewModel.CanLoadMoreModCatalog)
        {
            return;
        }

        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >=
            scrollViewer.Extent.Height - 220)
        {
            viewModel.LoadMoreModCatalogCommand.Execute(null);
        }
    }

    private void OnCloseModDeleteFlyoutClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is not Control control)
            return;

        var popup = control.FindAncestorOfType<Popup>() ??
            control.GetLogicalAncestors().OfType<Popup>().FirstOrDefault();
        if (popup is not null)
            popup.IsOpen = false;
    }
}
