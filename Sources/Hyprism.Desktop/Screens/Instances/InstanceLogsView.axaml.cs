// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceLogsView : UserControl
{
    private INotifyPropertyChanged? _viewModel;

    public InstanceLogsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public void SetMaximumWidth(double value)
        => InstanceLogsSection.MaxWidth = value;

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Dispatcher.UIThread.Post(ScrollLogsToBottom, DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstancesViewModel.LogsRevision))
            Dispatcher.UIThread.Post(ScrollLogsToBottom, DispatcherPriority.Loaded);
    }

    private void OnLogsScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.Y == 0 ||
            sender is not ScrollViewer scrollViewer ||
            DataContext is not InstancesViewModel viewModel ||
            !viewModel.IsLogsAutoScroll)
        {
            return;
        }

        var distanceFromBottom = scrollViewer.Extent.Height -
                                 scrollViewer.Offset.Y -
                                 scrollViewer.Viewport.Height;
        if (distanceFromBottom > 24)
            viewModel.IsLogsAutoScroll = false;
    }

    private void OnLogsContentPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Handled || DataContext is not InstancesViewModel viewModel)
            return;

        if (args.Source is Visual source &&
            (source is ListBoxItem || source.GetVisualAncestors().OfType<ListBoxItem>().Any()))
        {
            return;
        }

        viewModel.LogsSearchQuery = string.Empty;
        LogsContentHost.Focus();
    }

    private void OnLogsSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Escape || DataContext is not InstancesViewModel viewModel)
            return;

        viewModel.LogsSearchQuery = string.Empty;
        LogsContentHost.Focus();
        args.Handled = true;
    }

    private async void OnLogsListKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Source is TextBox || args.Key != Key.C ||
            (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 ||
            LogsList.SelectedItems is null)
        {
            return;
        }

        var textBlock = args.Source as SelectableTextBlock ??
            (args.Source as Visual)?.FindAncestorOfType<SelectableTextBlock>();
        if (textBlock is not null && !string.IsNullOrEmpty(textBlock.SelectedText))
        {
            return;
        }

        var lines = LogsList.SelectedItems.OfType<InstanceLogLineViewModel>()
            .Select(line => string.Join("\t", line.Time, line.Level, line.Source, line.Text));
        var text = string.Join(Environment.NewLine, lines);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (text.Length == 0 || clipboard is null)
            return;

        await clipboard.SetTextAsync(text);
        args.Handled = true;
    }

    private void ScrollLogsToBottom()
    {
        if (DataContext is not InstancesViewModel { IsLogsAutoScroll: true } viewModel ||
            viewModel.LogsLines.Count == 0)
        {
            return;
        }

        var scrollViewer = LogsList.GetVisualDescendants().OfType<SmoothScrollViewer>().FirstOrDefault();
        if (scrollViewer is not null)
        {
            scrollViewer.Offset = new Vector(
                scrollViewer.Offset.X,
                Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height));
            return;
        }

        LogsList.ScrollIntoView(viewModel.LogsLines[^1]);
    }

}
