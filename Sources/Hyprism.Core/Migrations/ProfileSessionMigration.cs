// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Hyprism.Core.Accounts;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;

namespace Hyprism.Core.Migrations;

/// <summary>
/// Moves an old root-level official session into its selected profile folder.
/// </summary>
public sealed class ProfileSessionMigration
{
    private readonly AppPathConfiguration _appPath;
    private readonly IConfigStore _configStore;

    /// <summary>Creates the profile session migration</summary>
    public ProfileSessionMigration(AppPathConfiguration appPath, IConfigStore configStore)
    {
        _appPath = appPath;
        _configStore = configStore;
    }

    /// <summary>
    /// Copies a valid legacy session, marks its selected profile as official, and only then removes the source.
    /// </summary>
    public void Migrate()
    {
        try
        {
            var selectedId = _configStore.Configuration.SelectedProfileId;
            if (string.IsNullOrWhiteSpace(selectedId))
                return;

            var legacyPath = TokenStore.GetLegacySessionFilePath(_appPath.AppDir);
            if (!File.Exists(legacyPath))
                return;

            var profilesPath = LauncherJsonFile.GetPath(
                LauncherUtilities.GetProfilesRoot(_appPath.AppDir),
                "Profiles.json",
                "profiles.json");
            if (!File.Exists(profilesPath))
                return;

            var profiles = JsonSerializer.Deserialize<List<Profile>>(
                File.ReadAllText(profilesPath),
                JsonDefaults.CaseInsensitiveIndented) ?? [];
            var profile = profiles.FirstOrDefault(candidate => candidate.Id == selectedId);
            if (profile is null)
                return;

            var profileFolder = LauncherUtilities.GetProfileFolderPath(_appPath.AppDir, profile);
            var destinationPath = TokenStore.GetSessionFilePath(profileFolder, _appPath.AppDir);
            if (File.Exists(destinationPath))
                return;

            var session = TokenStore.Load(legacyPath);
            if (session is null)
            {
                Logger.Warning("Migration", "Legacy official session is invalid and was left in place");
                return;
            }

            Directory.CreateDirectory(profileFolder);
            File.Copy(legacyPath, destinationPath);
            profile.IsOfficial = true;
            File.WriteAllText(profilesPath, JsonSerializer.Serialize(profiles, JsonDefaults.CaseInsensitiveIndented));
            File.Delete(legacyPath);
            Logger.Info("Migration", $"Moved official session into profile '{profile.Name}'");
        }
        catch (Exception exception)
        {
            Logger.Warning("Migration", $"Profile session migration failed: {exception.Message}");
        }
    }
}
