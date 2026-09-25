// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class ModalTextFieldTests
{
    [AvaloniaFact]
    public void ErrorStateChangesEditorSurfaceAndIndicator()
    {
        var input = new TextBox { Text = "invalid value" };
        var field = new ModalTextField
        {
            Content = input,
            ErrorMessage = "Invalid value"
        };
        var window = new Window
        {
            Width = 560,
            Height = 120,
            Content = field
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var indicator = Assert.Single(
            field.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("modalTextFieldError"));
        Assert.False(indicator.IsVisible);
        Assert.Equal(new Thickness(12, 9, 44, 9), input.Padding);

        field.IsError = true;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.True(indicator.IsVisible);
        Assert.Equal("Invalid value", ToolTip.GetTip(indicator));
        Assert.Equal(
            Color.Parse("#322525"),
            Assert.IsAssignableFrom<ISolidColorBrush>(input.Background).Color);

        field.IsError = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(indicator.IsVisible);

        window.Close();
    }
}
