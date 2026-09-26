// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Sources;

/// <summary>
/// Static helpers for constructing <see cref="MirrorMeta"/> objects from auto-discovery
/// results and for deriving stable mirror identifiers and names from URIs.
/// All business logic for <em>detecting</em> the mirror type remains in
/// <see cref="MirrorDiscovery"/>; this class only deals with schema construction.
/// </summary>
internal static class MirrorSchemaInferrer
{
    #region URI Helpers

    /// <summary>
    /// Produces a safe, lowercase identifier from the given <paramref name="uri"/>'s hostname
    /// by stripping common TLD/www decorations.
    /// </summary>
    /// <returns>The generated mirror identifier</returns>
    public static string GenerateMirrorId(Uri uri)
    {
        var host = uri.Host.Replace(".", "-").ToLowerInvariant();
        host = host.Replace("www-", "")
                   .Replace("-com", "")
                   .Replace("-org", "")
                   .Replace("-net", "");
        return host;
    }

    /// <summary>
    /// Extracts a human-readable mirror name from the <paramref name="uri"/>'s hostname
    /// (second-to-last label, capitalised).
    /// </summary>
    /// <returns>The mirror display name</returns>
    public static string ExtractMirrorName(Uri uri)
    {
        var host = uri.Host;
        var parts = host.Split('.');
        if (parts.Length >= 2)
        {
            var name = parts[^2];
            if (name.Equals("www", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
                name = parts[^3];
            return char.ToUpper(name[0]) + name[1..];
        }
        return host;
    }

    #endregion

    #region MirrorMeta Builders

    /// <summary>Builds a source that discovers protected full PWR files with HEAD requests.</summary>
    /// <param name="baseUri">Mirror address entered by the user.</param>
    /// <param name="pathPrefix">Path before the platform and branch directories.</param>
    /// <returns>The discovered mirror definition.</returns>
    public static MirrorMeta CreateHeadProbePatternMirror(Uri baseUri, string pathPrefix)
    {
        var baseUrl = baseUri.GetLeftPart(UriPartial.Authority);
        var filePattern = $"{{base}}{pathPrefix}/{{os}}/{{arch}}/{{branch}}/0/{{version}}.pwr";

        return new MirrorMeta
        {
            Id = GenerateMirrorId(baseUri),
            Name = ExtractMirrorName(baseUri),
            Description = $"Auto-discovered mirror from {baseUri.Host}",
            SourceType = "pattern",
            Headers = new Dictionary<string, string>
            {
                ["User-Agent"] = "{hytaleAgent}",
                ["x-hytale-launcher-version"] = "{hytaleVersion}",
                ["x-hytale-launcher-branch"] = "release"
            },
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = baseUrl,
                FullBuildUrl = filePattern,
                SignatureUrl = filePattern + ".sig",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "head-probe",
                    MaxProbeVersion = 256,
                    MinFileSizeBytes = 1_048_576
                }
            },
            SpeedTest = new MirrorSpeedTestConfig
            {
                PingUrl = $"{baseUrl}{pathPrefix}/linux/amd64/release/0/1.pwr",
                PingTimeoutSeconds = 5
            },
            Cache = new MirrorCacheConfig
            {
                IndexTtlMinutes = 30,
                SpeedTestTtlMinutes = 60
            }
        };
    }

    /// <summary>
    /// Builds a <see cref="MirrorMeta"/> for mirrors that expose version info via
    /// <c>/infos</c> and host patch archives under <c>/dl/{os}/{arch}/{version}.pwr</c>.
    /// </summary>
    /// <returns>The created infos api pattern mirror</returns>
    public static MirrorMeta CreateInfosApiPatternMirror(Uri baseUri, string mirrorId)
    {
        var baseUrl = baseUri.GetLeftPart(UriPartial.Authority);

        return new MirrorMeta
        {
            SchemaVersion = 1,
            Id = mirrorId,
            Name = ExtractMirrorName(baseUri),
            Description = $"Auto-discovered mirror from {baseUri.Host}",
            Priority = 100,
            Enabled = true,
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                FullBuildUrl = "{base}/dl/{os}/{arch}/{version}.pwr",
                DiffPatchUrl = "{base}/dl/{os}/{arch}/{version}.pwr",
                BaseUrl = baseUrl,
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "json-api",
                    Url = "{base}/infos",
                    JsonPath = "{os}-{arch}.{branch}.newest"
                },
                BranchMapping = [],
                OsMapping = new Dictionary<string, string>
                {
                    ["linux"] = "linux",
                    ["windows"] = "windows",
                    ["macos"] = "darwin"
                },
                ArchMapping = new Dictionary<string, string>
                {
                    ["x64"] = "amd64",
                    ["amd64"] = "amd64",
                    ["arm64"] = "arm64"
                },
                DiffBasedBranches = []
            },
            SpeedTest = new MirrorSpeedTestConfig
            {
                PingUrl = baseUrl + "/infos",
                PingTimeoutSeconds = 5
            },
            Cache = new MirrorCacheConfig
            {
                IndexTtlMinutes = 30,
                SpeedTestTtlMinutes = 60
            }
        };
    }

    /// <summary>
    /// Builds a <see cref="MirrorMeta"/> for mirrors that publish a <c>manifest.json</c>
    /// listing patch files in <c>{os}/{arch}/{branch}/{from}_to_{to}.pwr</c> layout.
    /// </summary>
    /// <returns>The created manifest pattern mirror</returns>
    public static MirrorMeta CreateManifestPatternMirror(
        Uri baseUri,
        string mirrorId,
        string baseUrl,
        string manifestUrl,
        HashSet<string> branches)
    {
        return new MirrorMeta
        {
            SchemaVersion = 1,
            Id = mirrorId,
            Name = ExtractMirrorName(baseUri),
            Description = $"Auto-discovered mirror from {baseUri.Host}",
            Priority = 100,
            Enabled = true,
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                FullBuildUrl = "{base}/{os}/{arch}/{branch}/0_to_{version}.pwr",
                DiffPatchUrl = "{base}/{os}/{arch}/{branch}/{from}_to_{to}.pwr",
                BaseUrl = baseUrl,
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "manifest",
                    Url = manifestUrl
                },
                OsMapping = new Dictionary<string, string>
                {
                    ["linux"] = "linux",
                    ["windows"] = "windows",
                    ["macos"] = "darwin"
                },
                ArchMapping = new Dictionary<string, string>
                {
                    ["x64"] = "amd64",
                    ["amd64"] = "amd64",
                    ["arm64"] = "arm64"
                },
                BranchMapping = [],
                DiffBasedBranches = [.. branches]
            },
            SpeedTest = new MirrorSpeedTestConfig
            {
                PingUrl = manifestUrl,
                PingTimeoutSeconds = 5
            },
            Cache = new MirrorCacheConfig
            {
                IndexTtlMinutes = 30,
                SpeedTestTtlMinutes = 60
            }
        };
    }

    /// <summary>
    /// Builds a <see cref="MirrorMeta"/> for mirrors running the Hytale Launcher API
    /// (<c>/launcher/patches/{branch}/versions</c>).
    /// </summary>
    /// <returns>The created launcher api pattern mirror</returns>
    public static MirrorMeta CreateLauncherApiPatternMirror(Uri baseUri, string mirrorId)
    {
        var baseUrl = baseUri.GetLeftPart(UriPartial.Authority);

        return new MirrorMeta
        {
            SchemaVersion = 1,
            Id = mirrorId,
            Name = ExtractMirrorName(baseUri),
            Description = $"Auto-discovered mirror from {baseUri.Host}",
            Priority = 100,
            Enabled = true,
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                FullBuildUrl = "{base}/launcher/patches/{os}/{arch}/{branch}/0/{version}.pwr",
                DiffPatchUrl = "{base}/launcher/patches/{os}/{arch}/{branch}/{from}/{to}.pwr",
                BaseUrl = baseUrl,
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "json-api",
                    Url = "{base}/launcher/patches/{branch}/versions?os_name={os}&arch={arch}",
                    JsonPath = "items[].version"
                },
                BranchMapping = new Dictionary<string, string>
                {
                    ["pre-release"] = "prerelease"
                },
                DiffBasedBranches = []
            },
            SpeedTest = new MirrorSpeedTestConfig
            {
                PingUrl = baseUrl + "/health"
            },
            Cache = new MirrorCacheConfig
            {
                IndexTtlMinutes = 30,
                SpeedTestTtlMinutes = 60
            }
        };
    }

    /// <summary>
    /// Builds a <see cref="MirrorMeta"/> for mirrors serving raw patch archives via HTTP
    /// directory auto-index at <c>{base}{basePath}/{os}/{arch}/{branch}/{from}/</c>.
    /// </summary>
    /// <returns>The created static files pattern mirror</returns>
    public static MirrorMeta CreateStaticFilesPatternMirror(Uri baseUri, string mirrorId, string basePath)
    {
        var baseUrl = baseUri.GetLeftPart(UriPartial.Authority);
        var pathPart = string.IsNullOrEmpty(basePath) ? "" : basePath;

        return new MirrorMeta
        {
            SchemaVersion = 1,
            Id = mirrorId,
            Name = ExtractMirrorName(baseUri),
            Description = $"Auto-discovered mirror from {baseUri.Host}",
            Priority = 100,
            Enabled = true,
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                FullBuildUrl = $"{{base}}{pathPart}/{{os}}/{{arch}}/{{branch}}/0/{{version}}.pwr",
                DiffPatchUrl = $"{{base}}{pathPart}/{{os}}/{{arch}}/{{branch}}/{{from}}/{{to}}.pwr",
                SignatureUrl = $"{{base}}{pathPart}/{{os}}/{{arch}}/{{branch}}/0/{{version}}.pwr.sig",
                BaseUrl = baseUrl,
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "html-autoindex",
                    Url = $"{{base}}{pathPart}/{{os}}/{{arch}}/{{branch}}/0/",
                    HtmlPattern = @"<a\s+href=""(\d+)\.pwr"">\d+\.pwr</a>\s+\S+\s+\S+\s+(\d+)",
                    MinFileSizeBytes = 1_048_576
                },
                DiffBasedBranches = []
            },
            SpeedTest = new MirrorSpeedTestConfig
            {
                PingUrl = baseUrl + pathPart
            },
            Cache = new MirrorCacheConfig
            {
                IndexTtlMinutes = 30,
                SpeedTestTtlMinutes = 60
            }
        };
    }

    #endregion

    #region JSON Structure Helpers

    /// <summary>
    /// Inspects the <c>hytale</c> JSON node of a mirror index API response and returns
    /// <c>"grouped"</c> when platform nodes contain sub-keys (<c>base</c> / <c>patch</c>),
    /// or <c>"flat"</c> otherwise.
    /// </summary>
    /// <returns>The detected JSON structure</returns>
    public static string DetectJsonStructure(JsonElement hytaleNode)
    {
        foreach (var branch in hytaleNode.EnumerateObject())
        {
            if (branch.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var platform in branch.Value.EnumerateObject())
                {
                    if (platform.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (platform.Value.TryGetProperty("base", out _) ||
                            platform.Value.TryGetProperty("patch", out _))
                        {
                            return "grouped";
                        }
                    }
                }
            }
        }
        return "flat";
    }
    #endregion
}
