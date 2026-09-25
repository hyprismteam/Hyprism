// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Game.Launch;

namespace Hyprism.Desktop.Screens.Instances;

public sealed record InstanceLogLineViewModel(
    string Level, string Time, string Source, string Text, bool IsTrace)
{
    internal GameConsoleLine OriginalLine { get; init; } = null!;

    public bool IsError => Level == "ERROR";

    public bool IsWarning => Level == "WARN";

    public bool IsSystem => Level == "INFO";
}

public sealed record InstanceListOptionViewModel(string Value, string Display);
