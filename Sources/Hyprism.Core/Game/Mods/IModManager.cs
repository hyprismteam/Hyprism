// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Mods;

/// <summary>
/// Provides mod management functionality including search, installation, and update checking
/// </summary>
public interface IModManager
{
    /// <summary>
    /// Ensures that the instance has a usable <c>UserData/Mods</c> directory
    /// </summary>
    /// <param name="instancePath">The root path of the game instance</param>
    /// <returns>The absolute path to the Mods directory</returns>
    string GetOrCreateModsDirectory(string instancePath);

    /// <summary>
    /// Searches for mods based on query and filters
    /// </summary>
    /// <param name="query">The search query string</param>
    /// <param name="page">The page number (0-based)</param>
    /// <param name="pageSize">The number of results per page</param>
    /// <param name="categories">Array of category slugs to filter by</param>
    /// <param name="sortField">The field to sort by (CurseForge sort index)</param>
    /// <param name="sortOrder">The sort order (ascending or descending)</param>
    /// <returns>A result containing matching mods and pagination info</returns>
    Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder);

    /// <summary>Searches a selected mod catalog.</summary>
    /// <param name="source">Catalog identifier.</param>
    /// <param name="query">Search text.</param>
    /// <param name="page">Zero-based page number.</param>
    /// <param name="pageSize">Maximum number of results.</param>
    /// <param name="categories">Source category identifiers.</param>
    /// <param name="sortField">Sort field identifier.</param>
    /// <param name="sortOrder">Sort direction.</param>
    /// <returns>Matching projects and pagination information.</returns>
    Task<ModSearchResult> SearchModsAsync(string source, string query, int page, int pageSize, string[] categories, int sortField, int sortOrder);

    /// <summary>
    /// Gets the list of available mod categories
    /// </summary>
    /// <returns>A list of mod categories</returns>
    Task<List<ModCategory>> GetModCategoriesAsync();

    /// <summary>
    /// Downloads and installs a mod file to the specified game instance
    /// </summary>
    /// <param name="slugOrId">The mod slug or ID</param>
    /// <param name="fileIdOrVersion">The file ID or version to install</param>
    /// <param name="instancePath">The path to the game instance</param>
    /// <param name="onProgress">Optional callback for progress updates (status, detail)</param>
    /// <returns><c>true</c> if installation succeeded; otherwise, <c>false</c></returns>
    Task<bool> InstallModFileToInstanceAsync(string slugOrId, string fileIdOrVersion, string instancePath, Action<string, string>? onProgress = null);

    /// <summary>Installs a file from the selected catalog into an instance.</summary>
    /// <param name="source">Catalog identifier.</param>
    /// <param name="modId">Project identifier.</param>
    /// <param name="fileId">File identifier.</param>
    /// <param name="instancePath">Instance directory.</param>
    /// <param name="onProgress">Optional progress callback.</param>
    /// <returns>Whether the file was installed.</returns>
    Task<bool> InstallModFileToInstanceAsync(string source, string modId, string fileId, string instancePath, Action<string, string>? onProgress = null);

    /// <summary>
    /// Gets the list of mods installed in a game instance
    /// </summary>
    /// <param name="instancePath">The path to the game instance</param>
    /// <returns>A list of installed mods</returns>
    List<InstalledMod> GetInstanceInstalledMods(string instancePath);

    /// <summary>
    /// Saves the installed mods list to the instance
    /// </summary>
    /// <param name="instancePath">The path to the game instance</param>
    /// <param name="mods">The list of installed mods to save</param>
    /// <returns>A task that completes after the mod list is written</returns>
    Task SaveInstanceModsAsync(string instancePath, List<InstalledMod> mods);

    /// <summary>
    /// Gets available files for a specific mod
    /// </summary>
    /// <param name="modId">The mod ID</param>
    /// <param name="page">The page number (0-based)</param>
    /// <param name="pageSize">The number of results per page</param>
    /// <returns>A result containing mod files and pagination info</returns>
    Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize);

    /// <summary>Gets files for a project in the selected catalog.</summary>
    /// <param name="source">Catalog identifier.</param>
    /// <param name="modId">Project identifier.</param>
    /// <param name="page">Zero-based page number.</param>
    /// <param name="pageSize">Maximum number of files.</param>
    /// <returns>Available files and pagination information.</returns>
    Task<ModFilesResult> GetModFilesAsync(string source, string modId, int page, int pageSize);

    /// <summary>
    /// Resolves the required dependencies declared by a specific mod file for catalog display
    /// </summary>
    /// <param name="modId">The CurseForge mod ID</param>
    /// <param name="fileId">The CurseForge file ID, or empty to resolve the best available file</param>
    /// <returns>Required dependencies with best-effort names, versions, and icon URLs</returns>
    Task<List<ModDependency>> GetModDependenciesAsync(string modId, string fileId);

    /// <summary>Gets required dependencies for a file in the selected catalog.</summary>
    /// <param name="source">Catalog identifier.</param>
    /// <param name="modId">Project identifier.</param>
    /// <param name="fileId">File identifier.</param>
    /// <returns>Required dependencies reported by the source.</returns>
    Task<List<ModDependency>> GetModDependenciesAsync(string source, string modId, string fileId);

    /// <summary>
    /// Gets a single mod's metadata from CurseForge by id or slug.
    /// Used to backfill missing slug/icon/description for installed mods
    /// </summary>
    /// <param name="modIdOrSlug">CurseForge numeric id or slug</param>
    /// <returns>Mod info when found; otherwise null</returns>
    Task<ModInfo?> GetModAsync(string modIdOrSlug);

    /// <summary>Gets a project from the selected catalog.</summary>
    /// <param name="source">Catalog identifier.</param>
    /// <param name="modIdOrSlug">Project identifier or slug.</param>
    /// <returns>Project details when available.</returns>
    Task<ModInfo?> GetModAsync(string source, string modIdOrSlug);

    /// <summary>
    /// Checks for available updates for mods installed in an instance
    /// </summary>
    /// <param name="instancePath">The path to the game instance</param>
    /// <returns>A list of mods that have updates available</returns>
    Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string instancePath);

    /// <summary>
    /// Gets the changelog for a specific mod file
    /// </summary>
    /// <param name="modId">The CurseForge mod ID (numeric)</param>
    /// <param name="fileId">The CurseForge file ID (numeric)</param>
    /// <returns>Changelog text (best-effort, may be empty)</returns>
    Task<string> GetModFileChangelogAsync(string modId, string fileId);

    /// <summary>
    /// Installs a mod from a local file
    /// </summary>
    /// <param name="sourcePath">The path to the local mod file</param>
    /// <param name="instancePath">The path to the game instance</param>
    /// <returns><c>true</c> if installation succeeded; otherwise, <c>false</c></returns>
    Task<bool> InstallLocalModFile(string sourcePath, string instancePath);

    /// <summary>
    /// Installs a mod from base64-encoded content
    /// </summary>
    /// <param name="fileName">The filename for the mod</param>
    /// <param name="base64Content">The base64-encoded mod file content</param>
    /// <param name="instancePath">The path to the game instance</param>
    /// <returns><c>true</c> if installation succeeded; otherwise, <c>false</c></returns>
    Task<bool> InstallModFromBase64(string fileName, string base64Content, string instancePath);

    /// <summary>
    /// Enables or disables an installed mod by renaming its file with or without the .disabled suffix
    /// </summary>
    /// <param name="instancePath">The path to the game instance</param>
    /// <param name="modId">The installed mod identifier</param>
    /// <param name="enabled">The desired enabled state</param>
    /// <returns><c>true</c> if the state changed; otherwise, <c>false</c></returns>
    Task<bool> SetModEnabledAsync(string instancePath, string modId, bool enabled);

    /// <summary>
    /// Removes an installed mod entry and its file from disk
    /// </summary>
    /// <param name="instancePath">The path to the game instance</param>
    /// <param name="modId">The installed mod identifier</param>
    /// <returns><c>true</c> if the mod was removed; otherwise, <c>false</c></returns>
    Task<bool> RemoveInstalledModAsync(string instancePath, string modId);
}
