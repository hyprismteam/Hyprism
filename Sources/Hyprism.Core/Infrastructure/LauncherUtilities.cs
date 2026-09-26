// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Runtime.InteropServices;
using Hyprism.Core.Models;

namespace Hyprism.Core.Infrastructure;

/// <summary>
/// Provides common utility methods for file operations, platform detection, and string manipulation.
/// </summary>
public static class LauncherUtilities
{
    private const string ProfileMigrationMarkerFileName = ".hyprism-profile-migrated";

    /// <summary>
    /// Normalizes version type strings to canonical forms.
    /// "prerelease" or "pre-release" -> "pre-release"
    /// "latest" -> "release"
    /// </summary>
    /// <returns>The normalized version type</returns>
    public static string NormalizeVersionType(string versionType)
    {
        if (string.IsNullOrWhiteSpace(versionType))
            return "release";
        if (versionType == "prerelease" || versionType == "pre-release")
            return "pre-release";
        if (versionType == "latest")
            return "release";
        return versionType;
    }

    /// <summary>
    /// Sanitizes a filename by removing invalid characters.
    /// Returns "default" if the resulting name is empty.
    /// </summary>
    /// <returns>The sanitized file name</returns>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    /// <summary>
    /// Gets the profiles root directory inside app data.
    /// </summary>
    /// <returns>The requested profiles root</returns>
    public static string GetProfilesRoot(string appDir)
    {
        var profilesDir = Path.Combine(appDir, "Profiles");
        Directory.CreateDirectory(profilesDir);
        return profilesDir;
    }

    /// <summary>
    /// Resolves profile folder path using ID-first layout and attempts to migrate
    /// legacy name-based folders when possible.
    /// </summary>
    /// <returns>The requested profile folder path</returns>
    public static string GetProfileFolderPath(string appDir, Profile profile, bool createIfMissing = true, bool migrateLegacyByName = true)
    {
        var profilesDir = GetProfilesRoot(appDir);
        var legacyPath = Path.Combine(profilesDir, SanitizeFileName(profile.Name ?? string.Empty));

        var hasValidId = !string.IsNullOrWhiteSpace(profile.Id) && Guid.TryParse(profile.Id, out _);
        if (!hasValidId)
        {
            if (createIfMissing && !Directory.Exists(legacyPath))
            {
                Directory.CreateDirectory(legacyPath);
            }
            return legacyPath;
        }

        var idPath = Path.Combine(profilesDir, profile.Id);
        var samePath = string.Equals(Path.GetFullPath(idPath), Path.GetFullPath(legacyPath), StringComparison.OrdinalIgnoreCase);

        if (migrateLegacyByName && !samePath)
        {
            TryMigrateLegacyProfileFolder(legacyPath, idPath);
        }

        if (createIfMissing && !Directory.Exists(idPath))
        {
            Directory.CreateDirectory(idPath);
        }

        return idPath;
    }

    /// <summary>
    /// Best-effort migration from a specific legacy profile folder to target ID folder.
    /// </summary>
    public static void TryMigrateSpecificProfileFolder(string sourceFolder, string targetFolder)
    {
        TryMigrateLegacyProfileFolder(sourceFolder, targetFolder);
    }

    private static void TryMigrateLegacyProfileFolder(string legacyPath, string idPath)
    {
        try
        {
            if (!Directory.Exists(legacyPath))
            {
                return;
            }

            if (!Directory.Exists(idPath))
            {
                Directory.Move(legacyPath, idPath);
                Logger.Info("Profile", $"Migrated profile folder to ID format: {legacyPath} -> {idPath}");
                return;
            }

            var markerPath = Path.Combine(legacyPath, ProfileMigrationMarkerFileName);
            if (File.Exists(markerPath))
            {
                return;
            }

            MergeDirectoryContentsMissingOnly(legacyPath, idPath);
            File.WriteAllText(markerPath, $"Migrated to {idPath} at {DateTime.UtcNow:o}");
            Logger.Warning("Profile", $"Legacy profile folder still exists after merge (left for safety): {legacyPath}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to migrate profile folder '{legacyPath}' to '{idPath}': {ex.Message}");
        }
    }

    private static void MergeDirectoryContentsMissingOnly(string sourceDir, string destDir)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists)
        {
            return;
        }

        Directory.CreateDirectory(destDir);

        foreach (var file in source.GetFiles())
        {
            var targetFilePath = Path.Combine(destDir, file.Name);
            if (!File.Exists(targetFilePath))
            {
                file.CopyTo(targetFilePath, false);
            }
        }

        foreach (var subDir in source.GetDirectories())
        {
            var newDestDir = Path.Combine(destDir, subDir.Name);
            MergeDirectoryContentsMissingOnly(subDir.FullName, newDestDir);
        }
    }

    /// <summary>
    /// Gets the effective application data directory.
    /// Checks environment variable first, then config file, then defaults to platform-specific location.
    /// </summary>
    /// <returns>The requested effective app dir</returns>
    public static string GetEffectiveAppDir()
    {
        var envDir = Environment.GetEnvironmentVariable("HYPRISM_DATA");
        if (!string.IsNullOrWhiteSpace(envDir) && Directory.Exists(envDir))
        {
            return envDir;
        }

        return GetDefaultAppDir();
    }

    /// <summary>
    /// Gets the default platform-specific application data directory.
    /// </summary>
    /// <returns>The requested default app dir</returns>
    public static string GetDefaultAppDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hyprism");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Hyprism");
        }
        else
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgDataHome))
            {
                return Path.Combine(xdgDataHome, "Hyprism");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Hyprism");
        }
    }

    /// <summary>
    /// Gets the current operating system identifier.
    /// </summary>
    /// <returns>The normalized operating system identifier</returns>
    public static string GetOS()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        return "unknown";
    }

    /// <summary>
    /// Gets the current CPU architecture identifier.
    /// </summary>
    /// <returns>The normalized processor architecture identifier</returns>
    public static string GetArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => "amd64"
        };
    }

    /// <summary>
    /// Runs a process silently without showing a console window.
    /// </summary>
    public static void RunSilentProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch (Exception ex)
        {
            Logger.Warning("Process", $"Failed to run {fileName} {arguments}: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears macOS quarantine attributes from a file or directory (macOS only).
    /// </summary>
    public static void ClearMacQuarantine(string path)
    {
        try
        {
            RunSilentProcess("xattr", $"-cr \"{path}\"");
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Failed to clear quarantine on {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively copies a directory and all its contents.
    /// Prevents infinite loops by checking source/dest paths.
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        var normalizedSource = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedDest = Path.GetFullPath(destinationDir).TrimEnd(Path.DirectorySeparatorChar);

        if (normalizedSource.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase)) return;
        if (normalizedDest.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        if (normalizedSource.StartsWith(normalizedDest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    /// <summary>
    /// Overload of CopyDirectory with overwrite parameter.
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
        {
            Logger.Warning("Files", $"Source directory does not exist: {sourceDir}");
            return;
        }

        Directory.CreateDirectory(destDir);

        foreach (var file in dir.GetFiles())
        {
            var targetPath = Path.Combine(destDir, file.Name);
            file.CopyTo(targetPath, overwrite);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var newDestDir = Path.Combine(destDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestDir, overwrite);
        }
    }

    /// <summary>
    /// Cleans up a corrupted game installation by preserving UserData and Client assets.
    /// </summary>
    public static void CleanupCorruptedInstall(string versionPath)
    {
        string backupRoot = Path.Combine(Path.GetTempPath(), "HyprismBackup", Guid.NewGuid().ToString());
        // Preserve UserData and Client/Assets to avoid re-downloading game
        string[] preserve = ["UserData", "Client"];

        try
        {
            Directory.CreateDirectory(backupRoot);
            foreach (var dirName in preserve)
            {
                var src = Path.Combine(versionPath, dirName);
                if (Directory.Exists(src))
                {
                    var dest = Path.Combine(backupRoot, dirName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    CopyDirectory(src, dest);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Download", $"Failed to backup user data before cleanup: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(versionPath))
            {
                Directory.Delete(versionPath, true);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Download", $"Failed to clean corrupted install at {versionPath}: {ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(versionPath);
            foreach (var dirName in preserve)
            {
                var backup = Path.Combine(backupRoot, dirName);
                var dest = Path.Combine(versionPath, dirName);
                if (Directory.Exists(backup))
                {
                    CopyDirectory(backup, dest);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Download", $"Failed to recreate install directory {versionPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the macOS app signature timestamp matches the executable's last write time.
    /// </summary>
    /// <returns>true when the macOS application signature is current; otherwise false</returns>
    public static bool IsMacAppSignatureCurrent(string executablePath, string stampPath)
    {
        try
        {
            if (!File.Exists(executablePath) || !File.Exists(stampPath))
            {
                return false;
            }

            var stamp = File.ReadAllText(stampPath).Trim();
            var currentTicks = File.GetLastWriteTimeUtc(executablePath).Ticks.ToString();
            return string.Equals(stamp, currentTicks, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Marks a macOS app as signed by recording the executable's timestamp.
    /// </summary>
    public static void MarkMacAppSigned(string executablePath, string stampPath)
    {
        try
        {
            var ticks = File.GetLastWriteTimeUtc(executablePath).Ticks.ToString();
            File.WriteAllText(stampPath, ticks);
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"Failed to record app sign stamp: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a random username for new users.
    /// Format: Adjective + Noun + 4-digit number (max 16 chars total)
    /// </summary>
    /// <returns>A generated random username</returns>
    public static string GenerateRandomUsername()
    {
        var random = new Random();

        var adjectives = new[] {
            "Happy", "Swift", "Brave", "Noble", "Quiet", "Bold", "Lucky", "Epic",
            "Jolly", "Lunar", "Solar", "Azure", "Royal", "Foxy", "Wacky", "Zesty",
            "Fizzy", "Dizzy", "Funky", "Jazzy", "Snowy", "Rainy", "Sunny", "Windy"
        };

        var nouns = new[] {
            "Panda", "Tiger", "Wolf", "Dragon", "Knight", "Ranger", "Mage", "Fox",
            "Bear", "Eagle", "Hawk", "Lion", "Falcon", "Raven", "Owl", "Shark",
            "Cobra", "Viper", "Lynx", "Badger", "Otter", "Pirate", "Ninja", "Viking"
        };

        var adj = adjectives[random.Next(adjectives.Length)];
        var noun = nouns[random.Next(nouns.Length)];
        var num = random.Next(1000, 9999);

        return $"{adj}{noun}{num}";
    }

    /// <summary>
    /// Loads environment variables from a .env file in the application base directory.
    /// Format: KEY=value (one per line, # for comments)
    /// </summary>
    public static void LoadEnvFile()
    {
        try
        {
            var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
            if (!File.Exists(envPath)) return;

            foreach (var line in File.ReadAllLines(envPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (value.StartsWith('"') && value.EndsWith('"'))
                        value = value[1..^1];
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
        catch { }
    }
}
