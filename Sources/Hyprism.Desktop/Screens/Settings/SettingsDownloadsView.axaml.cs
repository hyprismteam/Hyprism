// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsDownloadsView : UserControl
{
    public SettingsDownloadsView()
    {
        InitializeComponent();
    }

    private static void OnMirrorMenuPointerPressed(object? sender, PointerPressedEventArgs args)
        => args.Handled = true;

    private void OnToggleMirrorMenuPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is Border { DataContext: MirrorSourceViewModel mirror })
            mirror.IsMenuOpen = !mirror.IsMenuOpen;

        args.Handled = true;
    }

    private void OnCloseMirrorMenuClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MirrorSourceViewModel mirror })
            mirror.IsMenuOpen = false;
    }
}
