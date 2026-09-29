// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;

namespace Hyprism.Core.Models;

/// <summary>Paged result set returned from mod search operations</summary>
public class ModSearchResult
{
    /// <summary>Mods returned in this page of results</summary>
    public List<ModInfo> Mods { get; set; } = [];
    /// <summary>Total number of mods matching the search query</summary>
    public int TotalCount { get; set; }
}

/// <summary>Represents a mod available on CurseForge, normalized for launcher display</summary>
public class ModInfo
{
    /// <summary>Mod identifier (numeric CurseForge ID as string)</summary>
    public string Id { get; set; } = "";
    /// <summary>Display name of the mod</summary>
    public string Name { get; set; } = "";
    /// <summary>CurseForge URL slug</summary>
    public string Slug { get; set; } = "";
    /// <summary>Short summary / tagline</summary>
    public string Summary { get; set; } = "";
    /// <summary>Full HTML description</summary>
    public string Description { get; set; } = "";
    /// <summary>Primary author display name</summary>
    public string Author { get; set; } = "";
    /// <summary>URL of the primary author's CurseForge avatar</summary>
    public string AuthorAvatarUrl { get; set; } = "";
    /// <summary>Total download count on CurseForge</summary>
    public int DownloadCount { get; set; }
    /// <summary>URL of the mod icon image</summary>
    public string IconUrl { get; set; } = "";
    /// <summary>URL of the mod thumbnail image</summary>
    public string ThumbnailUrl { get; set; } = "";
    /// <summary>Category names the mod belongs to</summary>
    public List<string> Categories { get; set; } = [];
    /// <summary>ISO 8601 timestamp of the last mod file update</summary>
    public string DateUpdated { get; set; } = "";
    /// <summary>CurseForge file ID of the most recent release</summary>
    public string LatestFileId { get; set; } = "";
    /// <summary>Latest files returned with the search result</summary>
    public List<ModFileInfo> LatestFiles { get; set; } = [];
    /// <summary>Screenshots attached to the mod page</summary>
    public List<CurseForgeScreenshot> Screenshots { get; set; } = [];
}

/// <summary>Paged file list for a specific mod</summary>
public class ModFilesResult
{
    /// <summary>Files returned for the requested mod</summary>
    public List<ModFileInfo> Files { get; set; } = [];
    /// <summary>Total number of files matching the request</summary>
    public int TotalCount { get; set; }
}

/// <summary>Represents a single file/release of a mod</summary>
public class ModFileInfo
{
    /// <summary>CurseForge file identifier</summary>
    public string Id { get; set; } = "";
    /// <summary>Parent mod identifier</summary>
    public string ModId { get; set; } = "";
    /// <summary>Actual JAR or ZIP file name on disk</summary>
    public string FileName { get; set; } = "";
    /// <summary>Human-readable version label</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Direct download URL from CurseForge CDN</summary>
    public string DownloadUrl { get; set; } = "";
    /// <summary>File size in bytes</summary>
    public long FileLength { get; set; }
    /// <summary>ISO 8601 release date of the file</summary>
    public string FileDate { get; set; } = "";
    /// <summary>CurseForge release type: 1 = Release, 2 = Beta, 3 = Alpha</summary>
    public int ReleaseType { get; set; }
    /// <summary>Game version tags this file is compatible with</summary>
    public List<string> GameVersions { get; set; } = [];
    /// <summary>Download count for this specific file</summary>
    public int DownloadCount { get; set; }
    /// <summary>Dependencies and compatibility relations declared by this file</summary>
    public List<ModDependency> Dependencies { get; set; } = [];
}

/// <summary>CurseForge relation kinds used for mod dependencies</summary>
public enum CurseForgeDependencyRelationType
{
    /// <summary>The relation type was not provided by the source</summary>
    Unknown = 0,
    /// <summary>The related library is embedded in the file</summary>
    EmbeddedLibrary = 1,
    /// <summary>The related mod must not be installed with this file</summary>
    Incompatible = 2,
    /// <summary>The related mod may be installed but is not required</summary>
    OptionalDependency = 3,
    /// <summary>The related mod is required by this file</summary>
    RequiredDependency = 4,
    /// <summary>The relation points to a tool used with this file</summary>
    Tool = 5
}

/// <summary>A normalized dependency relation stored with an installed mod</summary>
public class ModDependency
{
    /// <summary>Related CurseForge project identifier</summary>
    public string ModId { get; set; } = "";
    /// <summary>Related CurseForge file identifier, when specified</summary>
    public string FileId { get; set; } = "";
    /// <summary>Relation kind</summary>
    public CurseForgeDependencyRelationType RelationType { get; set; }
    /// <summary>Resolved display name of the related mod</summary>
    public string Name { get; set; } = "";
    /// <summary>Resolved display version of the related mod file</summary>
    public string Version { get; set; } = "";
    /// <summary>Resolved icon URL of the related mod</summary>
    public string IconUrl { get; set; } = "";
}

/// <summary>A dependency declared by a Hytale <c>manifest.json</c></summary>
public class HytaleModDependency
{
    /// <summary>Hytale plugin identifier in the form <c>group:name</c></summary>
    public string Id { get; set; } = "";
    /// <summary>Version range declared by the plugin</summary>
    public string VersionRange { get; set; } = "";
}

/// <summary>A mod category returned from CurseForge</summary>
public class ModCategory
{
    /// <summary>CurseForge category identifier</summary>
    public int Id { get; set; }
    /// <summary>Category display name</summary>
    public string Name { get; set; } = "";
    /// <summary>CurseForge URL slug</summary>
    public string Slug { get; set; } = "";
}

/// <summary>Represents a mod that is installed in a game instance</summary>
public class InstalledMod
{
    /// <summary>Mod identifier (CurseForge numeric ID or local prefix)</summary>
    public string Id { get; set; } = "";
    /// <summary>Display name of the mod</summary>
    public string Name { get; set; } = "";
    /// <summary>CurseForge URL slug</summary>
    public string Slug { get; set; } = "";
    /// <summary>Installed version string</summary>
    public string Version { get; set; } = "";
    /// <summary>CurseForge file identifier of the installed file</summary>
    public string FileId { get; set; } = "";
    /// <summary>JAR file name on disk (without path)</summary>
    public string FileName { get; set; } = "";
    /// <summary>Whether the mod is enabled; disabled mods have a .disabled extension</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Primary author display name</summary>
    public string Author { get; set; } = "";
    /// <summary>Short description or summary</summary>
    public string Description { get; set; } = "";
    /// <summary>URL of the mod icon image</summary>
    public string IconUrl { get; set; } = "";
    /// <summary>Numeric CurseForge project ID (as string) for update-checking</summary>
    public string CurseForgeId { get; set; } = "";
    /// <summary>ISO 8601 release date of the installed file</summary>
    public string FileDate { get; set; } = "";

    /// <summary>
    /// CurseForge release type: 1 = Release, 2 = Beta, 3 = Alpha
    /// </summary>
    public int ReleaseType { get; set; } = 1;

    /// <summary>Screenshots attached to the mod page</summary>
    public List<CurseForgeScreenshot> Screenshots { get; set; } = [];

    /// <summary>
    /// The latest available file ID from CurseForge (for update checking).
    /// </summary>
    public string LatestFileId { get; set; } = "";

    /// <summary>
    /// The latest available version string from CurseForge (for update display).
    /// </summary>
    public string LatestVersion { get; set; } = "";

    /// <summary>
    /// Original file extension used before disabling (e.g. .jar or .zip).
    /// </summary>
    public string DisabledOriginalExtension { get; set; } = "";

    /// <summary>CurseForge relations declared by the installed file</summary>
    public List<ModDependency> Dependencies { get; set; } = [];

    /// <summary>Hytale plugin identifier read from the installed manifest</summary>
    public string ManifestId { get; set; } = "";

    /// <summary>Hytale plugin version read from the installed manifest</summary>
    public string ManifestVersion { get; set; } = "";

    /// <summary>Dependencies read from the installed Hytale manifest</summary>
    public List<HytaleModDependency> ManifestDependencies { get; set; } = [];

    /// <summary>Optional dependencies read from the installed Hytale manifest</summary>
    public List<HytaleModDependency> ManifestOptionalDependencies { get; set; } = [];

    /// <summary>Plugin identifiers that should load after this mod</summary>
    public List<string> ManifestLoadBefore { get; set; } = [];
}

/// <summary>
/// Entry for mod list import/export
/// </summary>
public class ModListEntry
{
    /// <summary>CurseForge project identifier, when known</summary>
    public string? CurseForgeId { get; set; }
    /// <summary>CurseForge file identifier, when known</summary>
    public string? FileId { get; set; }
    /// <summary>Mod display name stored in the list</summary>
    public string? Name { get; set; }
    /// <summary>Mod version stored in the list</summary>
    public string? Version { get; set; }
}

/// <summary>Describes an available update for an installed mod</summary>
public class ModUpdate
{
    /// <summary>CurseForge mod ID of the mod to update</summary>
    public string ModId { get; set; } = "";
    /// <summary>Currently installed file ID</summary>
    public string CurrentFileId { get; set; } = "";
    /// <summary>Latest available file ID on CurseForge</summary>
    public string LatestFileId { get; set; } = "";
    /// <summary>Latest available file name (for display)</summary>
    public string LatestFileName { get; set; } = "";
}
