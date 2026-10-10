// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;

namespace Hyprism.Desktop.Screens.Instances;

public sealed record InstanceWorldItemViewModel(
    string Name,
    string LastModified,
    string Size,
    Bitmap? Preview = null,
    string? PreviewPath = null,
    DateTime PreviewModifiedUtc = default,
    long PreviewLength = 0) : IDisposable
{
    public bool HasPreview => Preview is not null;

    public void Dispose() => Preview?.Dispose();
}
