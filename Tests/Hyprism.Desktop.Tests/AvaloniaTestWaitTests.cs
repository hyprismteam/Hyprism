// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class AvaloniaTestWaitTests
{
    [AvaloniaFact]
    public async Task PropertyAsyncRemembersTransientMatch()
    {
        var border = new Border { Opacity = 1 };
        var observation = AvaloniaTestWait.PropertyAsync(
            border,
            Visual.OpacityProperty,
            () => border.Opacity == 0,
            "transient opacity");

        border.Opacity = 0;
        border.Opacity = 1;

        await observation;
    }
}
