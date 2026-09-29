// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace Hyprism.Desktop.Controls;

/// <summary>Arranges one child inside a fixed aspect-ratio surface</summary>
public sealed class AspectRatioPanel : Panel
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioPanel, double>(nameof(Ratio), 16d / 9d);

    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ratio = GetSafeRatio();
        var width = availableSize.Width;
        var height = availableSize.Height;

        if (double.IsInfinity(width) && double.IsInfinity(height))
        {
            width = 16;
            height = 9;
        }
        else if (double.IsInfinity(width))
        {
            width = height * ratio;
        }
        else
        {
            height = width / ratio;
        }

        var desiredSize = new Size(Math.Max(0, width), Math.Max(0, height));
        foreach (var child in Children)
            child.Measure(desiredSize);

        return desiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var ratio = GetSafeRatio();
        var width = finalSize.Width;
        var height = width / ratio;
        if (height > finalSize.Height)
        {
            height = finalSize.Height;
            width = height * ratio;
        }

        var childBounds = new Rect(
            (finalSize.Width - width) / 2,
            (finalSize.Height - height) / 2,
            width,
            height);
        foreach (var child in Children)
            child.Arrange(childBounds);

        return finalSize;
    }

    private double GetSafeRatio()
        => double.IsFinite(Ratio) && Ratio > 0 ? Ratio : 16d / 9d;
}
