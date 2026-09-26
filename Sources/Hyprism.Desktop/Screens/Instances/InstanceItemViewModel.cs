// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;

namespace Hyprism.Desktop.Screens.Instances;

public sealed record InstanceItemViewModel(
    string Id,
    string Name,
    string Version,
    string Branch,
    bool IsInstalled,
    bool IsManaged,
    Bitmap? Icon = null)
{
    public bool HasIcon => Icon is not null;
    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "H"
        : Name[..1].ToUpperInvariant();
}
