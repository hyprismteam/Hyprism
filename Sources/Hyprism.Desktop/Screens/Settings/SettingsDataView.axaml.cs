// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Input;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsDataView : UserControl
{
    public SettingsDataView()
    {
        InitializeComponent();
    }

    private void OnInstanceFolderChangeActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.ArmInstanceFolderChangeCancellation();
    }
}
