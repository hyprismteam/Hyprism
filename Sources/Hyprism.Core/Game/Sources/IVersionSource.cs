// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Sources;

/// <summary>
/// Cached mirror speed test result
/// </summary>
public class MirrorSpeedTestResult
{
    /// <summary>Stable identifier of the tested mirror</summary>
    public string MirrorId { get; set; } = "";
    /// <summary>Base URL of the tested mirror</summary>
    public string MirrorUrl { get; set; } = "";
    /// <summary>Display name of the tested mirror</summary>
    public string MirrorName { get; set; } = "";
    /// <summary>Round-trip probe time in milliseconds, or a negative value when unavailable</summary>
    public long PingMs { get; set; } = 0;
    /// <summary>
    /// Download speed in MB/s (megabytes per second)
    /// </summary>
    public double SpeedMBps { get; set; } = 0;
    /// <summary>Whether the mirror responded successfully</summary>
    public bool IsAvailable { get; set; }
    /// <summary>
    /// Whether an available mirror exposes at least one version for the current OS and architecture.
    /// Null means the compatibility check does not apply or could not be completed
    /// </summary>
    public bool? HasVersionsForCurrentPlatform { get; set; }
    /// <summary>UTC time when the speed test was completed</summary>
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Type of version source
/// </summary>
public enum VersionSourceType
{
    /// <summary>
    /// Official Hytale servers (requires authenticated account)
    /// </summary>
    Official,

    /// <summary>
    /// Community mirror
    /// </summary>
    Mirror
}

/// <summary>
/// Human-readable source layout descriptor used for diagnostics and debugging.
/// Describes where full builds and patch files are fetched from, and how source-level caching works
/// </summary>
public sealed class VersionSourceLayoutInfo
{
    /// <summary>
    /// Description of full-build location/pattern
    /// </summary>
    public string FullBuildLocation { get; init; } = "";

    /// <summary>
    /// Description of patch location/pattern
    /// </summary>
    public string PatchLocation { get; init; } = "";

    /// <summary>
    /// Description of source-level cache policy and storage
    /// </summary>
    public string CachePolicy { get; init; } = "";
}

/// <summary>
/// Unified interface for version data sources.
/// Both official Hytale API and community mirrors implement this interface,
/// allowing the GameVersionCatalog to treat them uniformly
/// </summary>
public interface IVersionSource
{
    /// <summary>
    /// Unique identifier for this source (e.g., "hytale", "estrogen", "shipofyarn")
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Type of source (Official or Mirror)
    /// </summary>
    VersionSourceType Type { get; }

    /// <summary>
    /// Whether this source is currently available (e.g., authenticated for official)
    /// </summary>
    /// <remarks>For the official Hytale source, availability is based on any official profile with a valid session</remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Priority for merging versions (lower = higher priority).
    /// Official sources typically have priority 0, mirrors have higher numbers
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Diagnostic description of where full builds and patches come from,
    /// and how the source caches remote metadata
    /// </summary>
    VersionSourceLayoutInfo LayoutInfo { get; }

    /// <summary>
    /// Whether this source uses diff-based patches for a specific branch.
    /// Full builds remain available for fresh installs when a branch also has diffs.
    /// </summary>
    /// <param name="branch">The branch name</param>
    /// <remarks>For the official Hytale source, the latest full build is returned directly and patch chains update existing installations</remarks>
    /// <returns>True if the branch uses diff-based patches</returns>
    bool IsDiffBasedBranch(string branch);

    /// <summary>
    /// Fetches available versions with their download URLs for a specific platform and branch
    /// </summary>
    /// <param name="os">OS identifier ("windows", "darwin", "linux")</param>
    /// <param name="arch">Architecture ("amd64", "arm64")</param>
    /// <param name="branch">Branch name ("release", "pre-release")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of cached version entries with download URLs</returns>
    Task<List<CachedVersionEntry>> GetVersionsAsync(
        string os, string arch, string branch, CancellationToken ct = default);

    /// <summary>
    /// Gets the download URL for a specific version
    /// </summary>
    /// <param name="os">OS identifier</param>
    /// <param name="arch">Architecture</param>
    /// <param name="branch">Branch name</param>
    /// <param name="version">Version number</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Download URL, or null if not available</returns>
    Task<string?> GetDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default);

    /// <summary>
    /// Gets the diff patch URL between two consecutive versions (for pre-release)
    /// </summary>
    /// <param name="os">OS identifier</param>
    /// <param name="arch">Architecture</param>
    /// <param name="branch">Branch name</param>
    /// <param name="fromVersion">Source version</param>
    /// <param name="toVersion">Target version</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Diff patch URL, or null if not available</returns>
    Task<string?> GetDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default);

    /// <summary>
    /// Gets the full patch chain for a branch (all patch steps from version 1+).
    /// Used by GameVersionCatalog for centralized patch caching alongside version caching
    /// </summary>
    /// <param name="os">OS identifier</param>
    /// <param name="arch">Architecture</param>
    /// <param name="branch">Branch name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of patch steps, or empty list if not available</returns>
    Task<List<CachedPatchStep>> GetPatchChainAsync(
        string os, string arch, string branch, CancellationToken ct = default);

    /// <summary>
    /// Pre-fetches/warms the cache for this source
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A task that completes after the source cache is warmed</returns>
    /// <exception cref="OperationCanceledException">Thrown when preloading is cancelled</exception>
    Task PreloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Tests the speed and availability of this source
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Speed test result including ping, download speed, and availability</returns>
    Task<MirrorSpeedTestResult> TestSpeedAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks source availability and latency without downloading game data
    /// </summary>
    /// <param name="ct">Token used to cancel the network request</param>
    /// <returns>The current availability and request latency</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<MirrorSpeedTestResult> ProbeAvailabilityAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the cached speed test result if still valid
    /// </summary>
    /// <returns>Cached speed test result, or null if expired/missing</returns>
    MirrorSpeedTestResult? GetCachedSpeedTest();
}
