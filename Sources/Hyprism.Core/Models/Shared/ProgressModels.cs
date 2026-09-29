// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Core.Models;

/// <summary>
/// Represents a username->UUID mapping for the frontend.
/// </summary>
public class UuidMapping
{
    /// <summary>Offline username or Hytale display name</summary>
    public string Username { get; set; } = "";
    /// <summary>UUID associated with this username</summary>
    public string Uuid { get; set; } = "";
    /// <summary>Whether this mapping corresponds to the currently active profile</summary>
    public bool IsCurrent { get; set; } = false;
}

/// <summary>Reports the outcome of a file download operation</summary>
public class DownloadProgress
{
    /// <summary>Whether the download completed successfully</summary>
    public bool Success { get; set; }
    /// <summary>Progress percentage from 0 to 100</summary>
    public int Progress { get; set; }
    /// <summary>Error message if the download failed; null on success</summary>
    public string? Error { get; set; }
    /// <summary>Whether the download was cancelled by the user</summary>
    public bool Cancelled { get; set; }
}

/// <summary>Real-time progress update raised during a launcher operation</summary>
public class ProgressUpdateMessage
{
    /// <summary>Stable identifier of the instance this update belongs to, when applicable</summary>
    public string? InstanceId { get; set; }
    /// <summary>Operation state key (e.g. "downloading", "patching", "verifying")</summary>
    public string State { get; set; } = "unknown";
    /// <summary>Progress percentage from 0 to 100</summary>
    public double Progress { get; set; }
    /// <summary>i18n message key for the human-readable status string</summary>
    public string MessageKey { get; set; } = "common.loading";
    /// <summary>Format arguments interpolated into <see cref="MessageKey"/></summary>
    public object[]? Args { get; set; }
    /// <summary>Bytes downloaded so far in the current transfer</summary>
    public long DownloadedBytes { get; set; }
    /// <summary>Total expected bytes for the current transfer (0 if unknown)</summary>
    public long TotalBytes { get; set; }
}

/// <summary>Describes an operation error together with its optional instance target</summary>
public sealed class OperationErrorMessage
{
    /// <summary>Identifier of the affected instance, when applicable</summary>
    public string? InstanceId { get; init; }
    /// <summary>Stable error category key</summary>
    public required string Type { get; init; }
    /// <summary>User-facing error message or localization key</summary>
    public required string Message { get; init; }
    /// <summary>Optional technical details for diagnostics</summary>
    public string? Technical { get; init; }
}
