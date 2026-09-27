// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsLanguagePickerView : UserControl
{
    public static readonly StyledProperty<bool> ShowHeadingProperty =
        AvaloniaProperty.Register<SettingsLanguagePickerView, bool>(nameof(ShowHeading), true);

    public SettingsLanguagePickerView() => InitializeComponent();

    public bool ShowHeading
    {
        get => GetValue(ShowHeadingProperty);
        set => SetValue(ShowHeadingProperty, value);
    }
}
