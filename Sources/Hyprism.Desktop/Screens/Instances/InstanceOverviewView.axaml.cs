// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceOverviewView : UserControl
{
    public InstanceOverviewView()
    {
        InitializeComponent();
    }

    public Grid HubScreen => InstanceHubScreen;

    public StackPanel HubContent => InstanceHubContent;

    public Border CompactToolbar => CompactInstanceToolbar;

    public event Action<object?, RoutedEventArgs>? BackRequested;

    private void OnCompactInstanceBackClicked(object? sender, RoutedEventArgs args)
        => BackRequested?.Invoke(sender, args);

    private void OnManagedInstanceActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is InstancesViewModel viewModel)
            viewModel.ArmManagedInstanceCancellation();
    }

    private void OnCloseDeleteInstanceFlyoutClicked(object? sender, RoutedEventArgs args)
    {
        DeleteInstanceButton.Flyout?.Hide();
        CompactDeleteInstanceButton.Flyout?.Hide();
        CompactInstanceMenuPopup.IsRequestedOpen = false;
    }

    private void OnCloseCompactInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => CompactInstanceMenuPopup.IsRequestedOpen = false;

    private void OnToggleCompactInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => CompactInstanceMenuPopup.IsRequestedOpen = !CompactInstanceMenuPopup.IsRequestedOpen;
}
