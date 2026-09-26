// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Desktop.Controls;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class OverlayModalAnimationTests
{
    private const int WindowWidth = 900;
    private const int WindowHeight = 560;
    private const int ShoulderLeft = 146;
    private const int ShoulderRight = 754;
    private const int LineRow = 559;

    private static Color PixelColor(Window window, int x, int y)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hyprism-overlay-{Guid.NewGuid():N}.png");
        try
        {
            frame!.Save(path, PngBitmapEncoderOptions.Default);
            using var bitmap = SkiaSharp.SKBitmap.Decode(path);
            Assert.NotNull(bitmap);
            var color = bitmap!.GetPixel(x, y);
            return Color.FromRgb(color.Red, color.Green, color.Blue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Window CreateWindow(OverlayModal modal)
    {
        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Background = new SolidColorBrush(Color.Parse("#08090A"))
        };
        var sheet = new Grid { Width = 560, Height = 300 };
        sheet.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#08090A")),
            CornerRadius = new CornerRadius(18, 18, 0, 0),
            ClipToBounds = true
        });
        sheet.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#101114")),
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(18, 18, 0, 0),
            Margin = new Thickness(0, 0, 0, 42),
            IsHitTestVisible = false
        });
        modal.ModalContent = sheet;
        window.Content = new Grid
        {
            Children =
            {
                new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.Parse("#101114")),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom
                },
                modal
            }
        };
        return window;
    }

    private static void Freeze(
        Window window,
        OverlayModal modal,
        double backdropOpacity,
        double sheetOffset,
        double shoulderScale)
    {
        modal.IsVisible = true;
        var backdrop = modal.FindControl<Border>("OverlayModalBackdrop");
        var sheet = Assert.IsType<Grid>(modal.FindControl<Grid>("OverlayModalSheet"));
        var shoulders = Assert.IsType<Grid>(modal.FindControl<Grid>("OverlayModalShoulders"));
        backdrop!.Transitions = null;
        sheet.RenderTransform = new TranslateTransform { Y = sheetOffset };
        shoulders.RenderTransform = new ScaleTransform { ScaleY = shoulderScale };
        backdrop.Opacity = backdropOpacity;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task OwnsBackdropStateAndRestoresFocus()
    {
        var opener = new Button { Content = "Open" };
        var backdrop = new Grid { Children = { opener } };
        var entry = new TextBox();
        var modal = new OverlayModal
        {
            BackdropTarget = backdrop,
            InitialFocusTarget = entry,
            ModalContent = entry,
            HiddenOffset = 420
        };
        modal.DismissCommand = new RelayCommand(() => modal.IsOpen = false);
        var root = new Grid { Children = { backdrop, modal } };
        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Content = root
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        opener.Focus();
        Assert.Same(opener, window.FocusManager?.GetFocusedElement());

        modal.IsOpen = true;
        Assert.False(backdrop.IsHitTestVisible);
        await AvaloniaTestWait.UntilAsync(
            () => Assert.IsType<BlurEffect>(backdrop.Effect).Radius >= 5.99,
            "modal backdrop blur");
        await AvaloniaTestWait.UntilAsync(
            () => ReferenceEquals(window.FocusManager?.GetFocusedElement(), entry),
            "modal initial focus");

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await AvaloniaTestWait.UntilAsync(
            () => !modal.IsVisible && ReferenceEquals(window.FocusManager?.GetFocusedElement(), opener),
            "modal close and focus restoration");

        Assert.True(backdrop.IsHitTestVisible);
        Assert.Null(backdrop.Effect);
        window.Close();
    }

    [AvaloniaFact]
    public void ShouldersStaySyncedWithSheetMotion()
    {
        var modal = new OverlayModal
        {
            SheetMaxWidth = 560,
            SheetMaxHeight = 360,
            SheetMargin = new Thickness(12, 20, 12, 0),
            ShoulderMaxWidth = 608,
            ShoulderMargin = new Thickness(12, 0, 12, 0),
            HiddenOffset = 420
        };
        var window = CreateWindow(modal);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var stroke = Assert.IsAssignableFrom<ISolidColorBrush>(
            window.FindResource("MainCardStrokeBrush"));
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(
            window.FindResource("AppBackgroundBrush"));

        Freeze(window, modal, backdropOpacity: 0, sheetOffset: 420, shoulderScale: 0);
        Assert.Equal(stroke.Color, PixelColor(window, 100, LineRow));
        Assert.Equal(stroke.Color, PixelColor(window, ShoulderLeft + 9, LineRow));
        Assert.Equal(stroke.Color, PixelColor(window, 400, LineRow));
        Assert.Equal(stroke.Color, PixelColor(window, ShoulderRight - 9, LineRow));

        Freeze(window, modal, backdropOpacity: 1, sheetOffset: 8, shoulderScale: 0.9);
        Assert.NotEqual(stroke.Color, PixelColor(window, 100, LineRow));
        Assert.Equal(background.Color, PixelColor(window, 167, 558));
        Assert.Equal(background.Color, PixelColor(window, 400, LineRow));

        Freeze(window, modal, backdropOpacity: 0, sheetOffset: 100, shoulderScale: 0);
        Assert.Equal(stroke.Color, PixelColor(window, 100, LineRow));
        Assert.Equal(stroke.Color, PixelColor(window, ShoulderLeft + 9, LineRow));
        Assert.Equal(stroke.Color, PixelColor(window, ShoulderRight - 9, LineRow));
        Assert.Equal(background.Color, PixelColor(window, 400, LineRow));

        Freeze(window, modal, backdropOpacity: 1, sheetOffset: 0, shoulderScale: 1);
        Assert.NotEqual(stroke.Color, PixelColor(window, 100, LineRow));
        Assert.Equal(background.Color, PixelColor(window, 400, LineRow));
        Assert.Equal(background.Color, PixelColor(window, ShoulderLeft + 9, LineRow));
        Assert.Equal(background.Color, PixelColor(window, ShoulderRight - 9, LineRow));

        window.Close();
    }

    [AvaloniaFact]
    public async Task ShoulderScaleScheduleMatchesSheetTravel()
    {
        var modal = new OverlayModal
        {
            SheetMaxWidth = 560,
            SheetMaxHeight = 360,
            SheetMargin = new Thickness(12, 20, 12, 0),
            ShoulderMaxWidth = 608,
            ShoulderMargin = new Thickness(12, 0, 12, 0),
            HiddenOffset = 420
        };
        var window = CreateWindow(modal);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var transition = ShoulderScaleTransition(modal);
        Assert.NotNull(transition);

        modal.IsOpen = true;
        await WaitForShoulderScaleAsync(modal, 1);
        Assert.InRange(transition!.Delay.TotalMilliseconds, 130, 135);
        Assert.InRange(transition.Duration.TotalMilliseconds, 185, 190);

        modal.IsOpen = false;
        await WaitForShoulderScaleAsync(modal, 0);
        Assert.Equal(0, transition.Delay.TotalMilliseconds);
        Assert.InRange(transition.Duration.TotalMilliseconds, 185, 190);

        window.Close();
    }

    private static DoubleTransition? ShoulderScaleTransition(OverlayModal modal)
    {
        var shoulders = Assert.IsType<Grid>(modal.FindControl<Grid>("OverlayModalShoulders"));
        var scale = Assert.IsType<ScaleTransform>(shoulders.RenderTransform);
        return scale.Transitions?.OfType<DoubleTransition>().FirstOrDefault();
    }

    private static async Task WaitForShoulderScaleAsync(OverlayModal modal, double target)
    {
        var shoulders = Assert.IsType<Grid>(modal.FindControl<Grid>("OverlayModalShoulders"));
        await AvaloniaTestWait.UntilAsync(
            () => Assert.IsType<ScaleTransform>(shoulders.RenderTransform).ScaleY == target,
            $"modal shoulder scale to reach {target}");
    }
}
