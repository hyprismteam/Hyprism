// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text.Json;
using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Mods;

/// <summary>Reads dependency metadata from Hytale mod archives</summary>
internal static class HytaleModManifestReader
{
    private const long MaxManifestBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Reads the root <c>manifest.json</c> entry from a JAR or ZIP archive</summary>
    /// <returns>Parsed manifest metadata, or <see langword="null"/> when the archive has no readable manifest</returns>
    public static HytaleModManifestInfo? Read(string archivePath)
    {
        if (!File.Exists(archivePath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.Entries.FirstOrDefault(candidate =>
                candidate.FullName.Trim('/').Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length > MaxManifestBytes)
                return null;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var manifest = JsonSerializer.Deserialize<HytalePluginManifest>(json, JsonOptions);
            if (manifest is null)
                return null;

            var manifestId = BuildManifestId(manifest.Group, manifest.Name);
            return new HytaleModManifestInfo
            {
                ManifestId = manifestId,
                Version = manifest.Version ?? "",
                Dependencies = ToDependencies(manifest.Dependencies),
                OptionalDependencies = ToDependencies(manifest.OptionalDependencies),
                LoadBefore = manifest.LoadBefore?
                    .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                    .Select(identifier => identifier.Trim())
                    .ToList() ?? []
            };
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<HytaleModDependency> ToDependencies(Dictionary<string, string>? dependencies)
        => dependencies?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new HytaleModDependency
            {
                Id = pair.Key.Trim(),
                VersionRange = pair.Value?.Trim() ?? ""
            })
            .ToList() ?? [];

    private static string BuildManifestId(string? group, string? name)
    {
        if (string.IsNullOrWhiteSpace(group))
            return name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return group.Trim();
        return $"{group.Trim()}:{name.Trim()}";
    }

    private sealed class HytalePluginManifest
    {
        public string? Group { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public Dictionary<string, string>? Dependencies { get; set; }
        public Dictionary<string, string>? OptionalDependencies { get; set; }
        public List<string>? LoadBefore { get; set; }
    }
}

/// <summary>Normalized manifest metadata returned by <see cref="HytaleModManifestReader"/></summary>
internal sealed class HytaleModManifestInfo
{
    public string ManifestId { get; init; } = "";
    public string Version { get; init; } = "";
    public List<HytaleModDependency> Dependencies { get; init; } = [];
    public List<HytaleModDependency> OptionalDependencies { get; init; } = [];
    public List<string> LoadBefore { get; init; } = [];
}
