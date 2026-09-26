// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Input;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsDownloadsView : UserControl
{
    public SettingsDownloadsView()
    {
        InitializeComponent();
    }

    public event Action<Border, MirrorSourceViewModel>? MirrorMenuToggled;

    private static void OnMirrorMenuPointerPressed(object? sender, PointerPressedEventArgs args)
        => args.Handled = true;

    private void OnToggleMirrorMenuPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is Border { DataContext: MirrorSourceViewModel mirror } target)
            MirrorMenuToggled?.Invoke(target, mirror);

        args.Handled = true;
    }
}
