// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace Hyprism.Desktop.Controls;

/// <summary>
/// Presents a common form label, hint, and trailing editor layout
/// </summary>
public sealed class FormRow : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FormRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<FormRow, string?>(nameof(Hint));

    public static readonly StyledProperty<string?> ErrorProperty =
        AvaloniaProperty.Register<FormRow, string?>(nameof(Error));

    public static readonly DirectProperty<FormRow, bool> HasErrorProperty =
        AvaloniaProperty.RegisterDirect<FormRow, bool>(nameof(HasError), row => row.HasError);

    private bool _hasError;

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public string? Error
    {
        get => GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public bool HasError => _hasError;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ErrorProperty)
            SetAndRaise(HasErrorProperty, ref _hasError, !string.IsNullOrWhiteSpace(Error));
    }
}
