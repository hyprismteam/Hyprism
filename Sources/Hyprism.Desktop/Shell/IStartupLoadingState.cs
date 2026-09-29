// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Desktop.Shell;

/// <summary>
/// Minimal state required by the startup loading screen before launcher data is available.
/// </summary>
public interface IStartupLoadingState
{
    /// <summary>Whether the startup screen should cover the launcher shell</summary>
    bool IsStartupLoading { get; }

    /// <summary>Localized loading-screen heading</summary>
    string StartupLoadingTitle { get; }

    /// <summary>Localized loading-screen status</summary>
    string StartupLoadingStatus { get; }
}
