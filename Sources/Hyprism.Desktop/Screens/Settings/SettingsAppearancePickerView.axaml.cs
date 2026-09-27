// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hyprism.Desktop.Screens.Settings;

public sealed partial class SettingsAppearancePickerView : UserControl
{
    public static readonly StyledProperty<bool> IsWizardLayoutProperty =
        AvaloniaProperty.Register<SettingsAppearancePickerView, bool>(nameof(IsWizardLayout));

    public SettingsAppearancePickerView()
    {
        InitializeComponent();
    }

    public bool IsWizardLayout
    {
        get => GetValue(IsWizardLayoutProperty);
        set => SetValue(IsWizardLayoutProperty, value);
    }

    private void OnThemeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: SettingChoiceViewModel theme } &&
            DataContext is SettingsViewModel viewModel)
            viewModel.SelectTheme(theme);
    }

    private void OnAccentColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: AccentColorChoiceViewModel color } &&
            DataContext is SettingsViewModel viewModel)
            viewModel.SelectAccentColor(color);
    }
}
