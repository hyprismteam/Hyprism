// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hyprism.Core.Migrations;

/// <summary>
/// Moves the former default data directory before any services open launcher files.
/// </summary>
public static class AppDataDirectoryMigration
{
    private const string OldName = "HyPrism";
    private const string NewName = "Hyprism";
    private const string PendingName = ".HyPrism-rename-pending";
    private const string ConflictsName = ".HyPrism-merge-conflicts";

    /// <summary>Renames or merges the former default directory into the current one.</summary>
    public static void Migrate(string currentDirectory)
    {
        var current = Path.GetFullPath(currentDirectory);
        var parent = Path.GetDirectoryName(current)!;
        if (!string.Equals(Path.GetFileName(current), NewName, StringComparison.Ordinal))
            return;

        var old = Path.Combine(parent, OldName);
        var pending = Path.Combine(parent, PendingName);
        var movedPending = Directory.Exists(pending);
        if (Directory.Exists(pending))
            MoveOrMerge(pending, current);

        // Enumerating exact names distinguishes a case-only rename on Windows and macOS
        // from two separate directories on a case-sensitive file system.
        var oldEntry = Directory.Exists(parent)
            ? Directory.EnumerateDirectories(parent)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), OldName, StringComparison.Ordinal))
            : null;
        if (oldEntry is null)
        {
            if (movedPending)
                RewriteInstanceDirectory(current, old);
            return;
        }

        var currentEntry = Directory.EnumerateDirectories(parent)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), NewName, StringComparison.Ordinal));
        if (currentEntry is null && Directory.Exists(current))
        {
            Directory.Move(oldEntry, pending);
            MoveOrMerge(pending, current);
        }
        else
        {
            MoveOrMerge(oldEntry, current);
        }

        RewriteInstanceDirectory(current, old);
    }

    private static void MoveOrMerge(string source, string destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.Move(source, destination);
            return;
        }

        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Cannot merge launcher data through a symbolic link.");

        // A merge must rerun structural migrations for data from both roots.
        foreach (var root in new[] { source, destination })
        {
            var state = Directory.EnumerateFiles(root)
                .FirstOrDefault(path => Path.GetFileName(path).Equals("MigrationState.json", StringComparison.OrdinalIgnoreCase));
            if (state is not null)
                Archive(state, root, destination);
        }

        MergeDirectory(source, destination, source, destination);
    }

    private static void MergeDirectory(string source, string destination, string sourceRoot, string destinationRoot)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source).ToArray())
        {
            var target = Path.Combine(destination, Path.GetFileName(entry));
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) == 0 &&
                Path.GetExtension(entry).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                target = Directory.EnumerateFiles(destination)
                    .FirstOrDefault(path => Path.GetFileName(path)
                        .Equals(Path.GetFileName(entry), StringComparison.OrdinalIgnoreCase)) ?? target;
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!Directory.Exists(target) && !File.Exists(target))
                    Directory.Move(entry, target);
                else if ((attributes & FileAttributes.ReparsePoint) != 0 || File.Exists(target) ||
                         (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    Archive(entry, sourceRoot, destinationRoot);
                else
                {
                    MergeDirectory(entry, target, sourceRoot, destinationRoot);
                }

                continue;
            }

            if (!File.Exists(target) && !Directory.Exists(target))
            {
                File.Move(entry, target);
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, entry);
            if (relative.Equals("Config.json", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                Archive(target, destinationRoot, destinationRoot);
                File.Move(entry, target);
            }
            else
            {
                if (IsRegistry(relative) && File.Exists(target))
                    TryMergeRegistry(entry, target);
                Archive(entry, sourceRoot, destinationRoot);
            }
        }

        Directory.Delete(source);
    }

    private static bool IsRegistry(string relative) =>
        relative.Equals(Path.Combine("Profiles", "Profiles.json"), StringComparison.OrdinalIgnoreCase) ||
        relative.Equals(Path.Combine("Instances", "Instances.json"), StringComparison.OrdinalIgnoreCase);

    private static bool TryMergeRegistry(string source, string destination)
    {
        try
        {
            var oldEntries = JsonNode.Parse(File.ReadAllText(source))?.AsArray();
            var newEntries = JsonNode.Parse(File.ReadAllText(destination))?.AsArray();
            if (oldEntries is null || newEntries is null)
                return false;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in newEntries)
                if (entry is JsonObject item && GetId(item) is { } id)
                    ids.Add(id);

            foreach (var entry in oldEntries)
            {
                var id = entry is JsonObject item ? GetId(item) : null;
                if (id is not null ? ids.Add(id) : !newEntries.Any(existing => JsonNode.DeepEquals(existing, entry)))
                    newEntries.Add(entry?.DeepClone());
            }

            WriteJson(destination, newEntries);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? GetId(JsonObject item) =>
        item.FirstOrDefault(property => property.Key.Equals("Id", StringComparison.OrdinalIgnoreCase)).Value
            ?.GetValue<string>();

    private static void Archive(string path, string root, string destinationRoot)
    {
        var relative = Path.GetRelativePath(root, path);
        var archive = Path.Combine(destinationRoot, ConflictsName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        if (File.Exists(archive) || Directory.Exists(archive))
            archive += "." + Guid.NewGuid().ToString("N");

        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            Directory.Move(path, archive);
        else
            File.Move(path, archive);
    }

    private static void RewriteInstanceDirectory(string current, string old)
    {
        var configPath = Path.Combine(current, "Config.json");
        if (!File.Exists(configPath))
            configPath = Path.Combine(current, "config.json");
        if (!File.Exists(configPath))
            return;

        JsonObject? config;
        try
        {
            config = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return;
        }
        if (config is null)
            return;
        var key = config.Select(property => property.Key)
            .FirstOrDefault(name => name.Equals("InstanceDirectory", StringComparison.OrdinalIgnoreCase));
        if (key is null || config[key] is not JsonValue value || !value.TryGetValue<string>(out var path))
            return;

        var oldRoot = Path.GetFullPath(old);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
            (!path.Equals(oldRoot, StringComparison.Ordinal) &&
             !path.StartsWith(oldRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            return;

        config[key] = current + path[oldRoot.Length..];
        WriteJson(configPath, config);
    }

    private static void WriteJson(string path, JsonNode node)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
