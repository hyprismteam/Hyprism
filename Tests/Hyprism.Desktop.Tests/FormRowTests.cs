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

public sealed class FormRowTests
{
    [AvaloniaFact]
    public void UsesSharedSizeTokensAndPresentsOptionalError()
    {
        var row = new FormRow
        {
            Label = "Java path",
            Hint = "Choose a runtime",
            Error = "The selected file is unavailable",
            Content = new Button { Content = "Browse" }
        };
        row.Classes.Add("last");
        var window = new Window
        {
            Width = 560,
            Height = 140,
            Content = row
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Assert.IsType<double>(window.FindResource("Size.Row")), row.MinHeight);
        Assert.Equal(Assert.IsType<Thickness>(window.FindResource("Inset.Card")), row.Padding);
        Assert.Equal(new Thickness(0), row.BorderThickness);

        var text = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
        Assert.Contains(text, item => item.Text == "Java path");
        Assert.Contains(text, item => item.Text == "Choose a runtime");
        var error = Assert.Single(text, item => item.Text == "The selected file is unavailable");
        Assert.True(error.IsVisible);
        Assert.Equal(
            Color.Parse("#FF9696"),
            Assert.IsAssignableFrom<ISolidColorBrush>(error.Foreground).Color);
        Assert.Contains(row.GetVisualDescendants().OfType<Button>(), item => item.Content is "Browse");

        window.Close();
    }

    [AvaloniaFact]
    public void EmptyErrorDoesNotAddAnExtraLineToTheRow()
    {
        var emptyErrorRow = new FormRow
        {
            Label = "Close after launch",
            Hint = "Close launcher when game starts",
            Content = new Border { Width = 56, Height = 32 }
        };
        var errorRow = new FormRow
        {
            Label = "Show announcements",
            Hint = "Show launcher announcements received from Discord",
            Error = "This setting could not be saved",
            Content = new Border { Width = 56, Height = 32 }
        };
        var window = new Window
        {
            Width = 560,
            Height = 260,
            Content = new StackPanel
            {
                Children = { emptyErrorRow, errorRow }
            }
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var emptyError = Assert.Single(
            emptyErrorRow.GetVisualDescendants().OfType<TextBlock>(),
            item => item.Classes.Contains("formError"));
        Assert.False(emptyError.IsVisible);
        Assert.False(emptyErrorRow.HasError);
        Assert.True(errorRow.HasError);
        Assert.True(errorRow.Bounds.Height > emptyErrorRow.Bounds.Height);

        window.Close();
    }
}
