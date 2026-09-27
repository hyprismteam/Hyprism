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

    private static void OnInstanceMenuPointerPressed(object? sender, PointerPressedEventArgs args)
        => args.Handled = true;

    private static void OnToggleInstanceMenuPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is Border { DataContext: InstanceItemViewModel instance })
            instance.IsMenuOpen = !instance.IsMenuOpen;

        args.Handled = true;
    }

    private InstancesViewModel? SelectInstanceForMenuAction(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (DataContext is not InstancesViewModel viewModel ||
            sender is not Button { DataContext: InstanceItemViewModel instance })
            return null;

        instance.IsMenuOpen = false;
        viewModel.OpenInstanceDetailsCommand.Execute(instance.Id);
        return viewModel;
    }

    private void OnOpenInstanceFolderMenuClicked(object? sender, RoutedEventArgs args)
        => SelectInstanceForMenuAction(sender, args)?.OpenManagedInstanceFolderCommand.Execute(null);

    private void OnEditInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => SelectInstanceForMenuAction(sender, args)?.BeginEditManagedInstanceCommand.Execute(null);

    private void OnDeleteInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => SelectInstanceForMenuAction(sender, args)?.RequestManagedInstanceDeletionCommand.Execute(null);

    private void OnInstanceDragHandlePressed(object? sender, PointerPressedEventArgs args)
        => DragHandlePressed?.Invoke(sender, args);

    private void OnInstanceDragHandleMoved(object? sender, PointerEventArgs args)
        => DragHandleMoved?.Invoke(sender, args);

    private void OnInstanceDragHandleReleased(object? sender, PointerReleasedEventArgs args)
        => DragHandleReleased?.Invoke(sender, args);
}
