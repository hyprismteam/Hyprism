// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Instances;

/// <summary>
/// Manages game instances including installation paths, version tracking, and instance lifecycle
/// </summary>
public interface IInstanceRepository
{
    /// <summary>
    /// Raised after the instance collection changes: creation, deletion, import,
    /// rename, version change, reordering, selection change, or a disk resync.
    /// Subscribers should re-read <see cref="GetCachedInstances"/> for the new state.
    /// Not raised by in-place metadata writes such as <see cref="SaveInstanceMeta"/>
    /// </summary>
    event Action? InstancesChanged;

    /// <summary>
    /// Gets the root directory where all game instances are stored
    /// </summary>
    /// <returns>The absolute path to the instances root directory</returns>
    string GetInstanceRoot();

    /// <summary>
    /// Gets the directory path for a specific game branch
    /// </summary>
    /// <param name="branch">The game branch ("release" or "pre-release")</param>
    /// <returns>The absolute path to the branch directory</returns>
    string GetBranchPath(string branch);

    /// <summary>
    /// Gets the user data path for a specific game instance
    /// </summary>
    /// <param name="versionPath">The path to the game version directory</param>
    /// <returns>The absolute path to the user data directory</returns>
    string GetInstanceUserDataPath(string versionPath);

    /// <summary>
    /// Finds an existing instance path for the specified branch and version
    /// </summary>
    /// <param name="branch">The game branch</param>
    /// <param name="version">The numeric build identifier</param>
    /// <returns>The path to the existing instance, or <c>null</c> if not found</returns>
    string? FindExistingInstancePath(string branch, int version);

    /// <summary>
    /// Gets all instance root paths including legacy installation locations
    /// </summary>
    /// <returns>An enumerable of all instance root paths</returns>
    IEnumerable<string> GetInstanceRootsIncludingLegacy();

    /// <summary>
    /// Checks if the game client executable is present at the specified path
    /// </summary>
    /// <param name="versionPath">The path to the game version directory</param>
    /// <returns><c>true</c> if the client is present; otherwise, <c>false</c></returns>
    bool IsClientPresent(string versionPath);

    /// <summary>
    /// Checks if the game assets are present at the specified path
    /// </summary>
    /// <param name="versionPath">The path to the game version directory</param>
    /// <returns><c>true</c> if assets are present; otherwise, <c>false</c></returns>
    bool AreAssetsPresent(string versionPath);

    /// <summary>
    /// Gets the path for a specific game instance
    /// </summary>
    /// <param name="branch">The game branch</param>
    /// <param name="version">The version number</param>
    /// <returns>The absolute path to the instance directory</returns>
    string GetInstancePath(string branch, int version);

    /// <summary>
    /// Resolves the instance path, optionally preferring existing installations
    /// </summary>
    /// <param name="branch">The game branch</param>
    /// <param name="version">The version number</param>
    /// <param name="preferExisting">Whether to prefer existing installations over creating new paths</param>
    /// <returns>The resolved instance path</returns>
    string ResolveInstancePath(string branch, int version, bool preferExisting);

    /// <summary>
    /// Deletes a game instance from disk
    /// </summary>
    /// <param name="branch">The game branch</param>
    /// <param name="versionNumber">The version number to delete</param>
    /// <returns><c>true</c> if the instance was successfully deleted; otherwise, <c>false</c></returns>
    bool DeleteGame(string branch, int versionNumber);

    /// <summary>
    /// Deletes an instance directory from the current or a recognized legacy root by ID.
    /// If the directory is already missing, removes its stale registry entry
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <returns><c>true</c> if the instance was successfully deleted; otherwise, <c>false</c></returns>
    bool DeleteGameById(string instanceId);

    /// <summary>
    /// Gets a list of all installed game instances
    /// </summary>
    /// <returns>A list of installed instance metadata</returns>
    List<InstalledInstance> GetInstalledInstances();

    /// <summary>
    /// Sets or clears the custom name for an instance by branch and version
    /// </summary>
    /// <param name="branch">The game branch (e.g., "release", "pre-release")</param>
    /// <param name="version">The version number</param>
    /// <param name="customName">The custom name to set, or null to clear</param>
    void SetInstanceCustomName(string branch, int version, string? customName);

    /// <summary>
    /// Sets or clears the custom name for an instance by ID
    /// </summary>
    /// <param name="instanceId">The instance ID (GUID)</param>
    /// <param name="customName">The custom name to set, or null to clear</param>
    void SetInstanceCustomNameById(string instanceId, string? customName);

    /// <summary>
    /// Gets the instance metadata from the meta.json file
    /// </summary>
    /// <param name="instancePath">The path to the instance directory</param>
    /// <returns>The instance metadata, or null if not found</returns>
    InstanceMeta? GetInstanceMeta(string instancePath);

    /// <summary>
    /// Saves instance metadata to the meta.json file
    /// </summary>
    /// <param name="instancePath">The path to the instance directory</param>
    /// <param name="meta">The metadata to save</param>
    void SaveInstanceMeta(string instancePath, InstanceMeta meta);

    /// <summary>
    /// Creates a new instance with a generated ID
    /// </summary>
    /// <param name="branch">The game branch</param>
    /// <param name="version">The version number</param>
    /// <param name="name">Optional custom name for the instance</param>
    /// <param name="versionName">Optional human-readable game version name</param>
    /// <returns>The created instance metadata</returns>
    InstanceMeta CreateInstanceMeta(string branch, int version, string? name = null, string? versionName = null);

    /// <summary>
    /// Gets the currently selected instance based on SelectedInstanceId
    /// </summary>
    /// <returns>The selected instance info, or null if none selected</returns>
    InstanceInfo? GetSelectedInstance();

    /// <summary>
    /// Sets the selected instance by ID
    /// </summary>
    /// <param name="instanceId">The instance ID to select</param>
    void SetSelectedInstance(string instanceId);

    /// <summary>
    /// Synchronizes Config.Instances with the actual instance folders on disk.
    /// Removes entries for missing instances and adds entries for new ones
    /// </summary>
    void SyncInstancesWithConfig();

    /// <summary>
    /// Returns the in-memory/file-backed instance cache without rescanning the disk.
    /// Useful for fast lookups when a full sync is not required
    /// </summary>
    /// <returns>A snapshot of the cached instances</returns>
    List<InstanceInfo> GetCachedInstances();

    /// <summary>
    /// Persists the display order of the cached instances
    /// </summary>
    /// <param name="instanceIds">Instance IDs in the desired display order</param>
    void SetInstanceOrder(IReadOnlyList<string> instanceIds);

    /// <summary>
    /// Finds an instance by its ID
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <returns>The instance info, or null if not found</returns>
    InstanceInfo? FindInstanceById(string instanceId);

    /// <summary>
    /// Gets the instance path by its unique ID
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <returns>The absolute path to the instance directory, or null if not found</returns>
    string? GetInstancePathById(string instanceId);

    /// <summary>
    /// Finds an instance by branch and version
    /// </summary>
    /// <param name="branch">The game branch</param>
    /// <param name="version">The version number</param>
    /// <returns>The instance info, or null if not found</returns>
    InstanceInfo? FindInstanceByBranchAndVersion(string branch, int version);

    /// <summary>
    /// Creates a new instance directory with the given ID and returns the path
    /// </summary>
    /// <param name="branch">The game branch</param>
    /// <param name="instanceId">The unique instance ID (will be folder name)</param>
    /// <returns>The absolute path to the new instance directory</returns>
    string CreateInstanceDirectory(string branch, string instanceId);

    /// <summary>
    /// Changes the version/branch of an existing instance.
    /// For upgrades within the same branch: preserves game files and sets up for patching.
    /// For downgrades or branch changes: clears game client files and prepares for fresh download.
    /// Always keeps UserData and stores the selected target version in Meta.json
    /// </summary>
    /// <param name="instanceId">The unique instance ID</param>
    /// <param name="branch">The new game branch (e.g. "release")</param>
    /// <param name="version">The new version number</param>
    /// <returns>True if the operation succeeded</returns>
    bool ChangeInstanceVersion(string instanceId, string branch, int version);

    /// <summary>
    /// Imports a ZIP archive as a new game instance.
    /// Extracts the archive, reads meta.json for branch/version/id info,
    /// deduplicates instance IDs, and moves the contents to the instances directory
    /// </summary>
    /// <param name="zipPath">The path to the ZIP archive to import</param>
    /// <param name="cancellationToken">Token used to cancel extraction and metadata I/O</param>
    /// <returns>A task that completes after the instance is imported</returns>
    /// <exception cref="FileNotFoundException">Thrown when the archive does not exist</exception>
    /// <exception cref="InvalidDataException">Thrown when the archive does not contain a valid game instance</exception>
    Task ImportFromZipAsync(string zipPath, CancellationToken cancellationToken = default);
}
