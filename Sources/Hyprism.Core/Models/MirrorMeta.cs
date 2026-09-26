// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Core.Models;

/// <summary>
/// Root model for a mirror meta JSON file (*.mirror.json).
/// Describes how to discover and download game versions from a community mirror.
/// </summary>
public class MirrorMeta
{
    /// <summary>
    /// Schema version for forward compatibility. Current version: 1.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Unique identifier for this mirror (used as SourceId).
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Priority for source ordering (lower = higher priority). Official is 0, mirrors ≥ 100.
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this mirror is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Source type: "pattern" (URL template + version discovery) or "json-index" (single API returning full index).
    /// </summary>
    public string SourceType { get; set; } = "pattern";

    /// <summary>
    /// Configuration for sourceType "pattern".
    /// </summary>
    public MirrorPatternConfig? Pattern { get; set; }

    /// <summary>
    /// Configuration for sourceType "json-index".
    /// </summary>
    public MirrorJsonIndexConfig? JsonIndex { get; set; }

    /// <summary>
    /// Speed test configuration.
    /// </summary>
    public MirrorSpeedTestConfig SpeedTest { get; set; } = new();

    /// <summary>
    /// Cache configuration.
    /// </summary>
    public MirrorCacheConfig Cache { get; set; } = new();

    /// <summary>
    /// Custom HTTP headers to send with requests to this mirror.
    /// Format in UI: header=value or header="value with spaces"
    /// Supports {hytaleAgent} and {hytaleVersion} from the current official launcher release.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// Configuration for pattern-based mirrors where URLs are built from templates.
/// </summary>
public class MirrorPatternConfig
{
    /// <summary>
    /// URL template for full build downloads.
    /// Placeholders: {base}, {os}, {arch}, {branch}, {version}, {from}, {to}
    /// </summary>
    public string FullBuildUrl { get; set; } = "{base}/{os}/{arch}/{branch}/0/{version}.pwr";

    /// <summary>
    /// URL template for diff patches.
    /// </summary>
    public string? DiffPatchUrl { get; set; }

    /// <summary>
    /// URL template for signature files (optional).
    /// </summary>
    public string? SignatureUrl { get; set; }

    /// <summary>
    /// Base URL substituted into {base} placeholder.
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// How to discover available versions.
    /// </summary>
    public VersionDiscoveryConfig VersionDiscovery { get; set; } = new();

    /// <summary>
    /// Maps internal OS names to URL OS names. Only include overrides.
    /// </summary>
    public Dictionary<string, string>? OsMapping { get; set; }

    /// <summary>
    /// Maps internal arch names to URL arch names. Only include overrides.
    /// e.g. { "x64": "amd64" } to convert x64 to amd64 in URLs.
    /// </summary>
    public Dictionary<string, string>? ArchMapping { get; set; }

    /// <summary>
    /// Maps internal branch names to URL branch names. Only include overrides.
    /// </summary>
    public Dictionary<string, string>? BranchMapping { get; set; }

    /// <summary>
    /// Branches that can fall back to differential patches when a full build is unavailable.
    /// </summary>
    public List<string> DiffBasedBranches { get; set; } = [];
}

/// <summary>
/// How to discover available versions for a pattern-based mirror.
/// </summary>
public class VersionDiscoveryConfig
{
    /// <summary>
    /// Discovery method: "json-api", "html-autoindex", "manifest", "head-probe", or "static-list".
    /// </summary>
    public string Method { get; set; } = "json-api";

    /// <summary>
    /// URL for version listing. Supports {base}, {os}, {arch}, {branch} placeholders.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// For json-api: path to the version name or build in the JSON response.
    /// Supported formats: "items[].version", "versions", "$root"
    /// </summary>
    public string? JsonPath { get; set; }

    /// <summary>
    /// Optional path to the numeric build field when JSON entries expose both a version name and a build
    /// </summary>
    public string? BuildJsonPath { get; set; }

    /// <summary>
    /// For html-autoindex: regex pattern for extracting versions.
    /// Must have capture group 1 = version number. Group 2 = file size (optional).
    /// </summary>
    public string? HtmlPattern { get; set; }

    /// <summary>
    /// Minimum file size in bytes for html-autoindex filtering.
    /// </summary>
    public long MinFileSizeBytes { get; set; } = 0;

    /// <summary>
    /// Highest build to inspect when using head-probe discovery.
    /// </summary>
    public int MaxProbeVersion { get; set; } = 256;

    /// <summary>
    /// For static-list: explicit list of version numbers.
    /// </summary>
    public List<int>? StaticVersions { get; set; }
}

/// <summary>
/// Configuration for JSON-index-based mirrors (single API returning full file index).
/// </summary>
public class MirrorJsonIndexConfig
{
    /// <summary>
    /// URL of the API that returns the full index.
    /// </summary>
    public string ApiUrl { get; set; } = "";

    /// <summary>
    /// Root property name in the JSON response (e.g. "hytale").
    /// </summary>
    public string RootPath { get; set; } = "hytale";

    /// <summary>
    /// Index structure: "flat" (branch → platform → file→url) or "grouped" (branch → platform → base/patch → file→url).
    /// </summary>
    public string Structure { get; set; } = "flat";

    /// <summary>
    /// Maps internal OS names to JSON platform keys. Only include overrides (e.g. {"darwin": "mac"}).
    /// </summary>
    public Dictionary<string, string>? PlatformMapping { get; set; }

    /// <summary>
    /// Patterns for parsing file names in the index.
    /// </summary>
    public FileNamePatternConfig FileNamePattern { get; set; } = new();

    /// <summary>
    /// Branches that can fall back to differential patches when a full build is unavailable.
    /// </summary>
    public List<string> DiffBasedBranches { get; set; } = [];
}

/// <summary>
/// File name pattern configuration for JSON index mirrors.
/// </summary>
public class FileNamePatternConfig
{
    /// <summary>
    /// Pattern for full build file names. Default: "v{version}-{os}-{arch}.pwr"
    /// </summary>
    public string Full { get; set; } = "v{version}-{os}-{arch}.pwr";

    /// <summary>
    /// Pattern for diff patch file names. Default: "v{from}~{to}-{os}-{arch}.pwr"
    /// </summary>
    public string Diff { get; set; } = "v{from}~{to}-{os}-{arch}.pwr";
}

/// <summary>
/// Speed test configuration for a mirror.
/// </summary>
public class MirrorSpeedTestConfig
{
    /// <summary>
    /// URL for ping/availability check (HEAD request).
    /// </summary>
    public string? PingUrl { get; set; }

    /// <summary>
    /// Ping timeout in seconds.
    /// </summary>
    public int PingTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Size of data to download for speed test (bytes). Default: 10 MB.
    /// </summary>
    public int SpeedTestSizeBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Cache TTL configuration for a mirror.
/// </summary>
public class MirrorCacheConfig
{
    /// <summary>
    /// TTL for version index cache in minutes.
    /// </summary>
    public int IndexTtlMinutes { get; set; } = 30;

    /// <summary>
    /// TTL for speed test cache in minutes.
    /// </summary>
    public int SpeedTestTtlMinutes { get; set; } = 60;
}
