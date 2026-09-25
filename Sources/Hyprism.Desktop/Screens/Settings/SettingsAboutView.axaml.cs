// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsAboutView : UserControl
{
    public SettingsAboutView()
    {
        InitializeComponent();
    }

    private void OnAboutContributorsSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        const double containerPadding = 28;
        const double contributorSlotWidth = 64;

        if (DataContext is not SettingsViewModel viewModel || e.NewSize.Width <= containerPadding)
            return;

        var slots = Math.Max(
            1,
            (int)Math.Floor((e.NewSize.Width - containerPadding) / contributorSlotWidth));
        viewModel.UpdateAboutContributorCapacity(slots);
    }
}
