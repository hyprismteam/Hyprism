// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Xunit;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Hyprism.Desktop.Tests;

public sealed class SelectionControlTests
{
    [AvaloniaFact]
    public void SelectionControlsKeepKeyboardAndVisibleFocusBehavior()
    {
        const string longCheckLabel = "A long localized option label that wraps within a narrow settings rail.";
        const string firstRadioLabel = "Bundled runtime";
        var firstCheck = CreateCheckBox(longCheckLabel);
        var checkCommandCount = 0;
        firstCheck.Command = new RelayCommand(() => checkCommandCount++);
        var secondCheck = CreateCheckBox("Second option");
        var disabledCheck = CreateCheckBox("Unavailable option");
        disabledCheck.IsEnabled = false;
        var firstRadio = CreateRadioButton(firstRadioLabel);
        var secondRadio = CreateRadioButton("Custom runtime");
        var radioCommandCount = 0;
        firstRadio.Command = new RelayCommand(() => radioCommandCount++);
        secondRadio.Command = new RelayCommand(() => radioCommandCount++);
        var window = new Window
        {
            Width = 300,
            Height = 260,
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { firstCheck, secondCheck, disabledCheck, firstRadio, secondRadio }
            }
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        PressKey(window, Key.Tab, PhysicalKey.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(firstCheck, window.FocusManager?.GetFocusedElement());
        Assert.Equal(longCheckLabel, ControlAutomationPeer.CreatePeerForElement(firstCheck).GetName());
        var checkIndicator = Assert.Single(
            firstCheck.GetVisualDescendants().OfType<Border>(),
            item => item.Name == "SelectionIndicator");
        Assert.Equal(Color.Parse("#79B0F4"), Assert.IsAssignableFrom<ISolidColorBrush>(checkIndicator.BorderBrush).Color);
        Assert.True(firstCheck.GetVisualDescendants().OfType<TextBlock>().Single().Bounds.Height > 16);

        PressKey(window, Key.Space, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(firstCheck.IsChecked);
        Assert.Equal(1, checkCommandCount);
        Click(window, firstCheck);
        Assert.False(firstCheck.IsChecked);
        Assert.Equal(2, checkCommandCount);

        PressKey(window, Key.Tab, PhysicalKey.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(secondCheck, window.FocusManager?.GetFocusedElement());
        PressKey(window, Key.Space, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(secondCheck.IsChecked);
        Assert.False(disabledCheck.IsEffectivelyEnabled);
        Assert.Equal(0.45, disabledCheck.Opacity);
        Click(window, disabledCheck);
        Assert.False(disabledCheck.IsChecked);

        PressKey(window, Key.Tab, PhysicalKey.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(firstRadio, window.FocusManager?.GetFocusedElement());
        Assert.Equal(firstRadioLabel, ControlAutomationPeer.CreatePeerForElement(firstRadio).GetName());
        var radioIndicator = Assert.Single(
            firstRadio.GetVisualDescendants().OfType<Border>(),
            item => item.Classes.Contains("radioSelectionIndicator"));
        Assert.Equal(Color.Parse("#79B0F4"), Assert.IsAssignableFrom<ISolidColorBrush>(radioIndicator.BorderBrush).Color);
        PressKey(window, Key.Space, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(firstRadio.IsChecked);
        Assert.Equal(1, radioCommandCount);

        PressKey(window, Key.Tab, PhysicalKey.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(secondRadio, window.FocusManager?.GetFocusedElement());
        PressKey(window, Key.Space, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(secondRadio.IsChecked);
        Assert.False(firstRadio.IsChecked);
        Assert.Equal(2, radioCommandCount);
        Click(window, firstRadio);
        Assert.True(firstRadio.IsChecked);
        Assert.False(secondRadio.IsChecked);
        Assert.Equal(3, radioCommandCount);

        window.Close();
    }

    private static CheckBox CreateCheckBox(string label)
    {
        var checkBox = new CheckBox
        {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }
        };
        AutomationProperties.SetName(checkBox, label);
        checkBox.Classes.Add("uiSelectionCheck");
        checkBox.Classes.Add("row");
        return checkBox;
    }

    private static RadioButton CreateRadioButton(string label)
    {
        var radio = new RadioButton
        {
            GroupName = "JavaRuntime",
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new Border
                    {
                        Classes = { "radioSelectionIndicator" },
                        Child = new Ellipse { Classes = { "radioSelectionDot" } }
                    },
                    new TextBlock { Text = label }
                }
            }
        };
        AutomationProperties.SetName(radio, label);
        radio.Classes.Add("uiSelectionRadio");
        return radio;
    }

    private static void PressKey(Window window, Key key, PhysicalKey physicalKey, string? keySymbol = null)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, keySymbol);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, keySymbol);
    }

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
