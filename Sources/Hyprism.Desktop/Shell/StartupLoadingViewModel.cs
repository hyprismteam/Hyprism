// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Desktop.Localization;

namespace Hyprism.Desktop.Shell;

/// <summary>
/// Provides the existing startup screen while Core migrates local data.
/// </summary>
public sealed class StartupLoadingViewModel : IStartupLoadingState
{
    /// <summary>Creates the loading-screen state from the current localization</summary>
    public StartupLoadingViewModel(StringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        StartupLoadingTitle = localizer["startup.loading.title"];
        StartupLoadingStatus = localizer["startup.loading.content"];
    }

    /// <inheritdoc/>
    public bool IsStartupLoading => true;

    /// <inheritdoc/>
    public string StartupLoadingTitle { get; }

    /// <inheritdoc/>
    public string StartupLoadingStatus { get; }
}
