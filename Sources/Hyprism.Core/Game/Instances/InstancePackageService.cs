// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Instances;

/// <summary>The content selected for an instance package.</summary>
public enum InstancePackageKind
{
    /// <summary>Game, mods, and user data.</summary>
    Build,
    /// <summary>Mods and their metadata.</summary>
    Modpack,
    /// <summary>Game files without user data or mods.</summary>
    Game
}

/// <summary>The destination format for an instance package.</summary>
public enum InstancePackageFormat
{
    /// <summary>Metadata without file contents.</summary>
    Json,
    /// <summary>Metadata and selected file contents.</summary>
    Zip
}

/// <summary>Portable description of the selected instance content.</summary>
public sealed class InstancePackageManifest
{
    /// <summary>The package schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The selected content category.</summary>
    public InstancePackageKind Kind { get; set; }
    /// <summary>The source instance metadata.</summary>
    public InstanceMeta Instance { get; set; } = new();
    /// <summary>The selected files and their sizes.</summary>
    public List<InstancePackageFile> Files { get; set; } = [];
    /// <summary>The installed mod metadata when mods are included.</summary>
    public List<InstalledMod> Mods { get; set; } = [];
}

/// <summary>A file recorded in a package manifest.</summary>
public sealed class InstancePackageFile
{
    /// <summary>The path relative to the instance root.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>The file size in bytes.</summary>
    public long Size { get; set; }
}

/// <summary>Exports instance metadata or a portable archive and imports its metadata.</summary>
public static class InstancePackageService
{
    private const string ManifestName = "HyprismExport.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Writes a JSON manifest or ZIP archive to the destination atomically.</summary>
    /// <returns>A task that completes when the package has been written.</returns>
    public static async Task ExportAsync(
        string instancePath,
        InstanceMeta instance,
        InstancePackageKind kind,
        InstancePackageFormat format,
        string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Directory.Exists(instancePath))
            throw new DirectoryNotFoundException(instancePath);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var files = Directory.EnumerateFiles(instancePath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            .Select(path => (Source: path, Relative: Path.GetRelativePath(instancePath, path)
                .Replace(Path.DirectorySeparatorChar, '/')))
            .Where(file => !file.Relative.Equals("Meta.json", StringComparison.OrdinalIgnoreCase))
            .Where(file => Includes(kind, file.Relative))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToArray();
        var manifest = new InstancePackageManifest
        {
            Kind = kind,
            Instance = instance,
            Files = files.Select(file => new InstancePackageFile
            {
                Path = file.Relative,
                Size = new FileInfo(file.Source).Length
            }).ToList(),
            Mods = await ReadModsAsync(instancePath, kind, cancellationToken)
        };

        var temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (format == InstancePackageFormat.Json)
            {
                await using var output = File.Create(temporaryPath);
                await JsonSerializer.SerializeAsync(output, manifest, JsonOptions, cancellationToken);
            }
            else
            {
                await using var output = File.Create(temporaryPath);
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                await WriteJsonEntryAsync(archive, ManifestName, manifest, cancellationToken);
                await WriteJsonEntryAsync(archive, "Meta.json", new InstanceMeta
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = instance.Name,
                    Branch = instance.Branch,
                    Version = instance.Version,
                    VersionName = instance.VersionName,
                    CreatedAt = instance.CreatedAt,
                    LastPlayedAt = instance.LastPlayedAt,
                    PlayTimeSeconds = instance.PlayTimeSeconds,
                    InstalledVersion = kind == InstancePackageKind.Modpack ? 0 : instance.InstalledVersion,
                    PendingVersion = 0,
                    Notes = instance.Notes
                }, cancellationToken);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(file.Relative, CompressionLevel.Optimal);
                    if (!OperatingSystem.IsWindows())
                        entry.ExternalAttributes = (int)File.GetUnixFileMode(file.Source) << 16;
                    await using var source = File.OpenRead(file.Source);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Reads and validates an exported JSON manifest.</summary>
    /// <returns>The validated package manifest.</returns>
    public static async Task<InstancePackageManifest> ReadJsonAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<InstancePackageManifest>(
            input, JsonOptions, cancellationToken);
        if (manifest is not { SchemaVersion: 1, Instance: { Version: > 0 } } ||
            !Enum.IsDefined(manifest.Kind))
            throw new InvalidDataException("Unsupported or incomplete Hyprism instance package");
        return manifest;
    }

    private static bool Includes(InstancePackageKind kind, string path) => kind switch
    {
        InstancePackageKind.Build => true,
        InstancePackageKind.Modpack => path.StartsWith("UserData/Mods/", StringComparison.OrdinalIgnoreCase),
        InstancePackageKind.Game => !path.StartsWith("UserData/", StringComparison.OrdinalIgnoreCase) &&
                                    !path.StartsWith("Client/mods/", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static async Task<List<InstalledMod>> ReadModsAsync(
        string instancePath,
        InstancePackageKind kind,
        CancellationToken cancellationToken)
    {
        if (kind == InstancePackageKind.Game)
            return [];

        var path = LauncherJsonFile.GetPath(
            Path.Combine(instancePath, "UserData", "Mods"), "Manifest.json", "manifest.json");
        if (!File.Exists(path))
            return [];

        try
        {
            await using var input = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<InstalledMod>>(
                input, JsonOptions, cancellationToken) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var output = entry.Open();
        await JsonSerializer.SerializeAsync(output, value, JsonOptions, cancellationToken);
    }
}
