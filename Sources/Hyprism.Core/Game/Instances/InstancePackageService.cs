// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Instances;

/// <summary>The content selected for an instance package</summary>
public enum InstancePackageKind
{
    /// <summary>Game, mods, and user data</summary>
    Build,
    /// <summary>Mods and their metadata</summary>
    Modpack,
    /// <summary>Game files without user data or mods</summary>
    Game
}

/// <summary>The destination format for an instance package</summary>
public enum InstancePackageFormat
{
    /// <summary>Instance template without file contents.</summary>
    Json,
    /// <summary>Metadata and selected file contents</summary>
    Zip
}

/// <summary>Portable description of the selected instance content</summary>
public sealed class InstancePackageManifest
{
    /// <summary>The package schema version</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>The selected content category</summary>
    public InstancePackageKind Kind { get; set; }
    /// <summary>The source instance metadata</summary>
    public InstanceMeta Instance { get; set; } = new();
    /// <summary>The selected files and their sizes</summary>
    public List<InstancePackageFile> Files { get; set; } = [];
    /// <summary>The installed mod metadata when mods are included</summary>
    public List<InstalledMod> Mods { get; set; } = [];
}

/// <summary>Instance details restored by a JSON template.</summary>
public sealed class InstancePackageTemplate
{
    /// <summary>The JSON template schema version.</summary>
    public int SchemaVersion { get; set; }
    /// <summary>The user-facing instance details.</summary>
    public InstancePackageTemplateDetails Instance { get; set; } = new();
}

/// <summary>The instance details needed to create an uninstalled instance.</summary>
public sealed class InstancePackageTemplateDetails
{
    /// <summary>The instance display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>The game branch.</summary>
    public string Branch { get; set; } = string.Empty;
    /// <summary>The numeric game build.</summary>
    public int Version { get; set; }
    /// <summary>The optional display name of the game version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionName { get; set; }
    /// <summary>The optional user notes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }
}

/// <summary>A file recorded in a package manifest</summary>
public sealed class InstancePackageFile
{
    /// <summary>The path relative to the instance root</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>The file size in bytes</summary>
    public long Size { get; set; }
}

/// <summary>Exports an instance template or portable archive and reads templates for import.</summary>
public static class InstancePackageService
{
    private const string ManifestName = "HyprismExport.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Writes a JSON template or ZIP archive to the destination atomically.</summary>
    /// <remarks>Optional progress reports selected file bytes copied and reaches 100 after completion.</remarks>
    /// <returns>A task that completes when the package has been written.</returns>
    public static async Task ExportAsync(
        string instancePath,
        InstanceMeta instance,
        InstancePackageKind kind,
        InstancePackageFormat format,
        string destination,
        CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Directory.Exists(instancePath))
            throw new DirectoryNotFoundException(instancePath);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(kind));

        if (format == InstancePackageFormat.Json && kind == InstancePackageKind.Modpack)
            throw new ArgumentException("Modpack exports require a ZIP archive", nameof(kind));

        var temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0);
            if (format == InstancePackageFormat.Json)
            {
                await using var output = File.Create(temporaryPath);
                await JsonSerializer.SerializeAsync(output, new InstancePackageTemplate
                {
                    SchemaVersion = 2,
                    Instance = new InstancePackageTemplateDetails
                    {
                        Name = instance.Name,
                        Branch = instance.Branch,
                        Version = instance.Version,
                        VersionName = instance.VersionName,
                        Notes = kind == InstancePackageKind.Build ? instance.Notes : null
                    }
                }, JsonOptions, cancellationToken);
            }
            else
            {
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
                var totalBytes = manifest.Files.Sum(file => (double)file.Size);
                double copiedBytes = 0;
                var lastPercent = 0;
                var buffer = new byte[81920];
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(file.Relative, CompressionLevel.Optimal);
                    if (!OperatingSystem.IsWindows())
                        entry.ExternalAttributes = (int)File.GetUnixFileMode(file.Source) << 16;
                    await using var source = File.OpenRead(file.Source);
                    await using var target = entry.Open();
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        copiedBytes += read;
                        if (totalBytes <= 0)
                            continue;

                        var percent = Math.Min(99, (int)(copiedBytes * 100 / totalBytes));
                        if (percent <= lastPercent)
                            continue;
                        progress?.Report(percent);
                        lastPercent = percent;
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, overwrite: true);
            progress?.Report(100);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Reads a compact JSON template or a legacy schema 1 JSON manifest.</summary>
    /// <returns>The instance details to restore.</returns>
    public static async Task<InstancePackageTemplate> ReadJsonAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(path);
        var template = await JsonSerializer.DeserializeAsync<InstancePackageTemplate>(
            input, JsonOptions, cancellationToken);
        if (template is not { SchemaVersion: 1 or 2, Instance: { Version: > 0 } details } ||
            string.IsNullOrWhiteSpace(details.Name) ||
            string.IsNullOrWhiteSpace(details.Branch))
            throw new InvalidDataException("Unsupported or incomplete Hyprism instance package");
        return template;
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
