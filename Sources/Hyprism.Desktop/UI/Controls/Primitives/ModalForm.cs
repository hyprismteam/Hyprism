// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Hyprism.Desktop.Controls;

/// <summary>
/// Presents the shared heading, description, body, and action slots used by modal forms.
/// </summary>
public sealed class ModalForm : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ModalForm, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ModalForm, string?>(nameof(Description));

    public static readonly StyledProperty<string?> DismissLabelProperty =
        AvaloniaProperty.Register<ModalForm, string?>(nameof(DismissLabel));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<ModalForm, ICommand?>(nameof(DismissCommand));

    public static readonly StyledProperty<object?> ActionsContentProperty =
        AvaloniaProperty.Register<ModalForm, object?>(nameof(ActionsContent));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? DismissLabel
    {
        get => GetValue(DismissLabelProperty);
        set => SetValue(DismissLabelProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public object? ActionsContent
    {
        get => GetValue(ActionsContentProperty);
        set => SetValue(ActionsContentProperty, value);
    }
}
