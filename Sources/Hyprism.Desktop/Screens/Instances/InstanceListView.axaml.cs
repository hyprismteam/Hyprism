// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceListView : UserControl
{
    public InstanceListView()
    {
        InitializeComponent();
    }

    public Border Rail => InstancesListPane;

    public ItemsControl Items => InstancesItems;

    public event Action<object?, RoutedEventArgs>? InstanceClicked;

    public event Action<object?, RoutedEventArgs>? CreateRequested;

    public event Action<object?, PointerPressedEventArgs>? DragHandlePressed;

    public event Action<object?, PointerEventArgs>? DragHandleMoved;

    public event Action<object?, PointerReleasedEventArgs>? DragHandleReleased;

    private void OnInstanceClicked(object? sender, RoutedEventArgs args)
        => InstanceClicked?.Invoke(sender, args);

    private void OnOpenCreatorClicked(object? sender, RoutedEventArgs args)
        => CreateRequested?.Invoke(sender, args);

    private void OnInstanceDragHandlePressed(object? sender, PointerPressedEventArgs args)
        => DragHandlePressed?.Invoke(sender, args);

    private void OnInstanceDragHandleMoved(object? sender, PointerEventArgs args)
        => DragHandleMoved?.Invoke(sender, args);

    private void OnInstanceDragHandleReleased(object? sender, PointerReleasedEventArgs args)
        => DragHandleReleased?.Invoke(sender, args);
}
