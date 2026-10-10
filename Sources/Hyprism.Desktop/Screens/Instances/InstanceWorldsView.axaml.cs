// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceWorldsView : UserControl
{
    private InstanceWorldItemViewModel? _optionsWorld;

    public InstanceWorldsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => CloseOptionsMenu();
    }

    public bool CloseOptionsMenu()
    {
        var wasOpen = WorldOptionsPopup.IsRequestedOpen;
        WorldOptionsPopup.IsRequestedOpen = false;
        _optionsWorld = null;
        return wasOpen;
    }

    private void OnWorldOptionsClicked(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (sender is not Button { DataContext: InstanceWorldItemViewModel world } button)
            return;
        var wasOpen = WorldOptionsPopup.IsRequestedOpen && ReferenceEquals(WorldOptionsPopup.PlacementTarget, button);
        CloseOptionsMenu();
        if (wasOpen)
            return;
        _optionsWorld = world;
        WorldOptionsPopup.PlacementTarget = button;
        WorldOptionsPopup.IsRequestedOpen = true;
    }

    private void OnOpenWorldFolderClicked(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        var world = _optionsWorld;
        CloseOptionsMenu();
        (DataContext as InstancesViewModel)?.OpenWorldFolderCommand.Execute(world);
    }

    private void OnDeleteWorldClicked(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        var world = _optionsWorld;
        CloseOptionsMenu();
        (DataContext as InstancesViewModel)?.RequestWorldDeletionCommand.Execute(world);
    }
}
