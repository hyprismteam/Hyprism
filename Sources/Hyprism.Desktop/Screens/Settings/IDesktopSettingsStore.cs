// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Desktop.Screens.Settings;

/// <summary>
/// Exposes persisted preferences consumed by the Avalonia application
/// </summary>
public interface IDesktopSettingsStore
{
    /// <summary>Gets or sets the interface language code</summary>
    string Language { get; set; }

    /// <summary>Gets or sets whether background music is enabled</summary>
    bool MusicEnabled { get; set; }

    /// <summary>Gets or sets whether Desktop closes after starting the game</summary>
    bool CloseAfterLaunch { get; set; }

    /// <summary>Gets or sets whether Discord announcements are displayed</summary>
    bool ShowDiscordAnnouncements { get; set; }

    /// <summary>Gets or sets whether the news page is disabled</summary>
    bool DisableNews { get; set; }

    /// <summary>Gets or sets whether authenticated game mode is enabled</summary>
    bool OnlineMode { get; set; }

    /// <summary>Gets or sets the authentication service domain</summary>
    string AuthDomain { get; set; }

    /// <summary>Gets or sets user-added authentication service domains</summary>
    IReadOnlyList<string> AuthServers { get; set; }

    /// <summary>Gets or sets custom Java arguments</summary>
    string JavaArguments { get; set; }

    /// <summary>Gets or sets whether a custom Java executable is used</summary>
    bool UseCustomJava { get; set; }

    /// <summary>Gets or sets the custom Java executable path</summary>
    string CustomJavaPath { get; set; }

    /// <summary>Gets or sets the preferred GPU selection mode</summary>
    string GpuPreference { get; set; }

    /// <summary>Gets or sets custom environment variables passed to the game</summary>
    string GameEnvironmentVariables { get; set; }

    /// <summary>Gets the configured game instance root</summary>
    string InstanceDirectory { get; }

    /// <summary>Gets the default game instance root</summary>
    string DefaultInstanceDirectory { get; }

    /// <summary>Gets the launcher data root</summary>
    string LauncherDataDirectory { get; }

    /// <summary>
    /// Creates a HyprismLibrary root inside the selected directory and moves instance data
    /// </summary>
    /// <param name="path">Selected parent directory, or an empty value to restore the default root</param>
    /// <param name="cancellationToken">Cancellation requested by the active folder action</param>
    /// <param name="progress">Optional byte progress for files copied to the new root</param>
    /// <returns><see langword="true"/> when the root was changed successfully</returns>
    Task<bool> SetInstanceDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default,
        IProgress<InstanceDirectoryMoveProgress>? progress = null);

    /// <summary>
    /// Measures launcher and instance storage grouped by file purpose
    /// </summary>
    /// <param name="cancellationToken">Cancellation requested when the view is closed</param>
    /// <returns>Current storage usage</returns>
    Task<LauncherStorageUsage> GetLauncherStorageUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets or sets whether alpha mod releases are visible</summary>
    bool ShowAlphaMods { get; set; }
}

/// <summary>
/// Reports byte progress while instance data is copied to a new root
/// </summary>
/// <param name="BytesCopied">Bytes copied so far</param>
/// <param name="TotalBytes">Total bytes scheduled for copying</param>
public readonly record struct InstanceDirectoryMoveProgress(long BytesCopied, long TotalBytes)
{
    /// <summary>Gets the completed percentage clamped to the supported display range</summary>
    public int Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((int)Math.Round(BytesCopied * 100d / TotalBytes), 0, 100);
}
