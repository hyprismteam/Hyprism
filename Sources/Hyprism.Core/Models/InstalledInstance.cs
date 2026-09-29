// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Core.Models;

/// <summary>
/// Represents the validation status of a game instance.
/// </summary>
public enum InstanceValidationStatus
{
    /// <summary>All required files are present and the instance is ready to launch</summary>
    Valid,

    /// <summary>The instance directory exists but no game files are present</summary>
    NotInstalled,

    /// <summary>Critical files are missing or corrupted</summary>
    Corrupted,

    /// <summary>Validation status has not been checked yet</summary>
    Unknown
}

/// <summary>
/// Detailed information about what's missing or wrong with an instance.
/// </summary>
public class InstanceValidationDetails
{
    /// <summary>Whether the instance executable is present</summary>
    public bool HasExecutable { get; set; }
    /// <summary>Whether the instance assets directory is present</summary>
    public bool HasAssets { get; set; }
    /// <summary>Whether the instance libraries are present</summary>
    public bool HasLibraries { get; set; }
    /// <summary>Whether the instance configuration is present</summary>
    public bool HasConfig { get; set; }
    /// <summary>Names of required components that are missing</summary>
    public List<string> MissingComponents { get; set; } = [];
    /// <summary>Error description produced during validation</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents a locally installed game instance, including size and validation metadata.
/// </summary>
public class InstalledInstance
{
    /// <summary>Unique instance identifier (UUID)</summary>
    public string Id { get; set; } = "";
    /// <summary>Game branch this instance belongs to (e.g. "release" or "pre-release")</summary>
    public string Branch { get; set; } = "";
    /// <summary>Installed game version number</summary>
    public int Version { get; set; }
    /// <summary>Absolute path to the instance directory on disk</summary>
    public string Path { get; set; } = "";
    /// <summary>Whether the instance contains user-generated data (saves, etc.)</summary>
    public bool HasUserData { get; set; }
    /// <summary>Size in bytes of the user data within the instance</summary>
    public long UserDataSize { get; set; }
    /// <summary>Total size in bytes of the entire instance directory</summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// Legacy property for backwards compatibility.
    /// Use ValidationStatus for more detailed information.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Detailed validation status of the instance.
    /// </summary>
    public InstanceValidationStatus ValidationStatus { get; set; } = InstanceValidationStatus.Unknown;

    /// <summary>
    /// Detailed information about validation results.
    /// </summary>
    public InstanceValidationDetails? ValidationDetails { get; set; }

    /// <summary>Optional user-defined display name for the instance</summary>
    public string? CustomName { get; set; }
}
