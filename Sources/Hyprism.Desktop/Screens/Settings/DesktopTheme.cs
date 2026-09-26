// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Desktop.Screens.News;

namespace Hyprism.Desktop.Screens.Settings;

internal static class DesktopTheme
{
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(360);
    private static readonly Dictionary<SolidColorBrush, Color> OriginalColors = new(ReferenceEqualityComparer.Instance);
    private static readonly List<(SolidColorBrush Brush, Color From, Color To)> ActiveBrushes = [];
    private static DispatcherTimer? _transitionTimer;

    public static IReadOnlyList<AccentColorPreset> AccentColors { get; } =
    [
        new("blue", Color.Parse("#3584E4")),
        new("cyan", Color.Parse("#2190A4")),
        new("green", Color.Parse("#3A944A")),
        new("yellow", Color.Parse("#C88800")),
        new("orange", Color.Parse("#ED5B00")),
        new("red", Color.Parse("#E62D42")),
        new("pink", Color.Parse("#D56199")),
        new("purple", Color.Parse("#9141AC")),
        new("slate", Color.Parse("#6F8396"))
    ];

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => "light",
        "system" => "system",
        _ => "dark"
    };

    public static string NormalizeAccent(string? value)
        => AccentColors.FirstOrDefault(color => string.Equals(color.Id, value?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
           ?? "blue";

    public static void Apply(string? value)
    {
        if (Application.Current is not { } app)
            return;

        var requested = Normalize(value) switch
        {
            "light" => ThemeVariant.Light,
            "system" => ThemeVariant.Default,
            _ => ThemeVariant.Dark
        };

        if (app.RequestedThemeVariant == requested)
            return;

        if (app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } window } ||
            GetPalette(app, window.ActualThemeVariant) is not { } oldPalette)
        {
            app.RequestedThemeVariant = requested;
            return;
        }

        var oldTheme = window.ActualThemeVariant;
        var visibleColors = BrushColors(oldPalette);
        app.RequestedThemeVariant = requested;

        if (window.ActualThemeVariant == oldTheme ||
            GetPalette(app, window.ActualThemeVariant) is not { } newPalette)
            return;

        StopTransition();
        foreach (var (key, resource) in newPalette)
        {
            if (key is not string name || resource is not SolidColorBrush brush ||
                !visibleColors.TryGetValue(name, out var from))
                continue;

            if (!OriginalColors.TryGetValue(brush, out var to))
                OriginalColors[brush] = to = brush.Color;

            brush.Color = from;
            if (from != to)
                ActiveBrushes.Add((brush, from, to));
        }

        if (ActiveBrushes.Count == 0)
            return;

        StartTransition(window);
    }

    public static void ApplyAccent(string? value)
    {
        if (Application.Current is not { } app)
            return;

        var accent = AccentColors.First(color => color.Id == NormalizeAccent(value)).Color;
        var targets = new Dictionary<ThemeVariant, Dictionary<string, Color>>
        {
            [ThemeVariant.Dark] = AccentTargets(accent, light: false),
            [ThemeVariant.Light] = AccentTargets(accent, light: true)
        };

        var window = (app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var activePalette = window is { IsVisible: true }
            ? GetPalette(app, window.ActualThemeVariant)
            : null;
        var activeColors = activePalette is null ? [] : BrushColors(activePalette);
        var interrupted = ActiveBrushes.Select(item => (item.Brush, From: item.Brush.Color, item.To)).ToArray();
        StopTransition();

        foreach (var (variant, variantTargets) in targets)
        {
            if (GetPalette(app, variant) is not { } palette)
                continue;

            foreach (var (key, color) in variantTargets)
            {
                if (palette.TryGetValue(key, out var resource) && resource is SolidColorBrush brush)
                {
                    OriginalColors[brush] = color;
                    brush.Color = color;
                }
            }
        }

        if (activePalette is null || window is null)
            return;

        foreach (var (brush, from, oldTarget) in interrupted)
        {
            var to = OriginalColors.GetValueOrDefault(brush, oldTarget);
            brush.Color = from;
            if (from != to)
                ActiveBrushes.Add((brush, from, to));
        }

        if (!targets.TryGetValue(window.ActualThemeVariant, out var activeTargets))
            return;

        foreach (var (key, to) in activeTargets)
        {
            if (!activePalette.TryGetValue(key, out var resource) || resource is not SolidColorBrush brush ||
                ActiveBrushes.Any(item => ReferenceEquals(item.Brush, brush)) ||
                !activeColors.TryGetValue(key, out var from) || from == to)
                continue;

            brush.Color = from;
            ActiveBrushes.Add((brush, from, to));
        }

        if (ActiveBrushes.Count > 0)
            StartTransition(window);
    }

    private static Dictionary<string, Color> AccentTargets(Color accent, bool light)
    {
        var hover = Interpolate(accent, Colors.Black, 0.12);
        var linkTarget = light ? Colors.Black : Colors.White;
        var link = Interpolate(accent, linkTarget, light ? 0.25 : 0.42);
        var linkHover = Interpolate(accent, linkTarget, light ? 0.40 : 0.62);

        return new Dictionary<string, Color>
        {
            ["AccentBrush"] = accent,
            ["AccentForegroundBrush"] = Colors.White,
            ["AccentSoftBrush"] = Color.FromArgb(0x20, accent.R, accent.G, accent.B),
            ["AccentHoverBrush"] = hover,
            ["AccentMutedHoverBrush"] = hover,
            ["PrimaryHoverBrush"] = hover,
            ["PrimaryPressedBrush"] = Interpolate(accent, Colors.Black, 0.18),
            ["ArticleLinkBrush"] = link,
            ["ArticleLinkHoverBrush"] = linkHover,
            ["ArticleCodeForegroundBrush"] = Interpolate(accent, linkTarget, light ? 0.38 : 0.67),
            ["ArticleCodeBorderBrush"] = light
                ? Interpolate(accent, Colors.White, 0.68)
                : Color.FromArgb(0x66, accent.R, accent.G, accent.B),
            ["ArticleInlineCodeBrush"] = light
                ? Interpolate(accent, Colors.White, 0.90)
                : Color.FromArgb(0x2E, accent.R, accent.G, accent.B),
            ["ArticleInlineCodeBorderBrush"] = light
                ? Interpolate(accent, Colors.White, 0.60)
                : Color.FromArgb(0x80, accent.R, accent.G, accent.B),
            ["ArticleCodeBrush"] = light
                ? Interpolate(accent, Colors.White, 0.93)
                : Color.Parse("#E6151618")
        };
    }

    private static void StartTransition(Window window)
    {
        var watch = Stopwatch.StartNew();
        _transitionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _transitionTimer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(watch.Elapsed.TotalMilliseconds / TransitionDuration.TotalMilliseconds, 0, 1);
            var eased = progress * progress * (3 - 2 * progress);
            foreach (var (brush, from, to) in ActiveBrushes)
                brush.Color = Interpolate(from, to, eased);

            if (progress >= 1)
            {
                StopTransition();
                foreach (var article in window.GetVisualDescendants().OfType<NewsRichTextBlock>())
                    article.RefreshThemeColors();
            }
        };
        _transitionTimer.Start();
    }

    private static ResourceDictionary? GetPalette(Application app, ThemeVariant variant)
    {
        var semantic = app.Resources.MergedDictionaries
            .OfType<ResourceDictionary>()
            .FirstOrDefault(dictionary => dictionary.ThemeDictionaries.Count > 0);
        return semantic is not null && semantic.ThemeDictionaries.TryGetValue(variant, out var palette)
            ? palette as ResourceDictionary
            : null;
    }

    private static Dictionary<string, Color> BrushColors(ResourceDictionary palette)
        => palette
            .Where(entry => entry.Key is string && entry.Value is SolidColorBrush)
            .ToDictionary(entry => (string)entry.Key, entry => ((SolidColorBrush)entry.Value!).Color);

    private static void StopTransition()
    {
        _transitionTimer?.Stop();
        _transitionTimer = null;
        foreach (var (brush, _, to) in ActiveBrushes)
            brush.Color = to;
        ActiveBrushes.Clear();
    }

    private static Color Interpolate(Color from, Color to, double progress)
        => Color.FromArgb(
            Blend(from.A, to.A, progress),
            Blend(from.R, to.R, progress),
            Blend(from.G, to.G, progress),
            Blend(from.B, to.B, progress));

    private static byte Blend(byte from, byte to, double progress)
        => (byte)Math.Round(from + (to - from) * progress);
}

internal sealed record AccentColorPreset(string Id, Color Color);
