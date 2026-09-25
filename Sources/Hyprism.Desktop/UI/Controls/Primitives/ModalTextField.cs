// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace Hyprism.Desktop.Controls;

/// <summary>
/// Hosts a modal text editor and exposes its validation state to the shared template.
/// </summary>
public sealed class ModalTextField : ContentControl
{
    public static readonly StyledProperty<bool> IsErrorProperty =
        AvaloniaProperty.Register<ModalTextField, bool>(nameof(IsError));

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<ModalTextField, string?>(nameof(ErrorMessage));

    public bool IsError
    {
        get => GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsErrorProperty)
            PseudoClasses.Set(":error", IsError);
    }
}
