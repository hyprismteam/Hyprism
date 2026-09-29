// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Migrations;

/// <summary>
/// Moves resumable game payloads from the legacy cache root into the game downloads cache.
/// </summary>
public static class GameDownloadCacheMigration
{
    private static readonly Lock MigrationLock = new();

    /// <summary>Moves legacy PWR payloads without touching unrelated cache content</summary>
    public static void Migrate(string appDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);

        lock (MigrationLock)
        {
            var cacheDirectory = Path.Combine(appDirectory, "Cache");
            if (!Directory.Exists(cacheDirectory))
                return;

            var legacyFiles = Directory
                .EnumerateFiles(cacheDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsGamePayload)
                .ToArray();
            if (legacyFiles.Length == 0)
                return;

            var downloadsDirectory = LauncherCachePaths.GetGameDownloadsDirectory(appDirectory);
            Directory.CreateDirectory(downloadsDirectory);

            foreach (var sourcePath in legacyFiles)
            {
                try
                {
                    MoveCacheFile(sourcePath, Path.Combine(
                        downloadsDirectory,
                        Path.GetFileName(sourcePath)));
                }
                catch (Exception exception)
                {
                    Logger.Warning(
                        "Cache",
                        $"Could not migrate game download {Path.GetFileName(sourcePath)}: {exception.Message}");
                }
            }
        }
    }

    private static bool IsGamePayload(string path)
        => path.EndsWith(".pwr", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith(".pwr.part", StringComparison.OrdinalIgnoreCase);

    private static void MoveCacheFile(string sourcePath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        var source = new FileInfo(sourcePath);
        var destination = new FileInfo(destinationPath);
        if (source.Length > destination.Length ||
            source.Length == destination.Length && source.LastWriteTimeUtc > destination.LastWriteTimeUtc)
        {
            File.Move(sourcePath, destinationPath, overwrite: true);
            return;
        }

        File.Delete(sourcePath);
    }
}
