// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Encodings.Web;
using System.Text.Json;
using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Migrations;
namespace Hyprism.Core.Accounts;

/// <summary>
/// Manages profile operations: creation, deletion, switching, and profile folder/symlink management
/// </summary>
public class JsonProfileRepository : IProfileRepository
{
    #region Fields and Constructor
    private readonly string _appDir;
    private readonly IConfigStore _configStore;
    private readonly ISkinRepository _skins;
    private readonly IInstanceRepository _instances;
    private readonly IUserIdentityProvider _identity;
    private bool _profileFolderMigrationAttempted;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonProfileRepository"/> class
    /// </summary>
    /// <param name="appPath">The application path configuration</param>
    /// <param name="configStore">The configuration service</param>
    /// <param name="skins">The skin management service</param>
    /// <param name="instances">The game instance service</param>
    /// <param name="identity">The user identity service</param>
    public JsonProfileRepository(
        AppPathConfiguration appPath,
        IConfigStore configStore,
        ISkinRepository skins,
        IInstanceRepository instances,
        IUserIdentityProvider identity)
    {
        _appDir = appPath.AppDir;
        _configStore = configStore;
        _skins = skins;
        _instances = instances;
        _identity = identity;

        EnsureProfileStorageUpgraded();
    }

    /// <inheritdoc/>
    public event Action? ProfilesChanged;

    private void RaiseProfilesChanged() => ProfilesChanged?.Invoke();

    #endregion

    #region Profile cache (Profiles.json)

    private static readonly JsonSerializerOptions _profileJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion

    /// <summary>Returns the path to the profile cache file inside the profiles folder</summary>
    private string GetProfileCachePath() =>
        LauncherJsonFile.GetPath(GetProfilesFolder(), "Profiles.json", "profiles.json");

    /// <summary>
    /// Loads the profile list from Profiles.json
    /// </summary>
    private List<Profile> LoadProfilesFromCache()
    {
        var path = GetProfileCachePath();
        if (File.Exists(path))
        {
            try
            {
                return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(path), _profileJsonOpts) ?? [];
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to read Profiles.json: {ex.Message}");
            }
        }

        return [];
    }

    /// <summary>Saves the profile list to Profiles.json</summary>
    private void SaveProfilesToCache(IEnumerable<Profile> profiles)
    {
        try
        {
            var list = profiles.ToList();
            var dir = GetProfilesFolder();
            Directory.CreateDirectory(dir);
            File.WriteAllText(GetProfileCachePath(), JsonSerializer.Serialize(list, _profileJsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to save Profiles.json: {ex.Message}");
        }
    }

    /// <summary>Gets the ID of the currently active profile</summary>
    /// <returns>The selected profile identifier, or null when unavailable</returns>
    public string GetSelectedProfileId() => _configStore.Configuration.SelectedProfileId ?? "";

    /// <summary>Gets the currently active profile object, or null if none is selected</summary>
    /// <returns>The selected profile, or null when unavailable</returns>
    public Profile? GetSelectedProfile()
    {
        var id = GetSelectedProfileId();
        if (string.IsNullOrEmpty(id)) return null;
        return LoadProfilesFromCache().FirstOrDefault(p => p.Id == id);
    }


    /// <inheritdoc/>
    public List<Profile> GetProfiles()
    {
        EnsureProfileStorageUpgraded();

        var profiles = LoadProfilesFromCache();

        var valid = profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.UUID))
            .ToList();

        if (valid.Count != profiles.Count)
        {
            Logger.Info("Profile", $"Cleaned up {profiles.Count - valid.Count} invalid profiles");
            SaveProfilesToCache(valid);
        }

        Logger.Info("Profile", $"GetProfiles returning {valid.Count} profiles");
        return valid;
    }

    /// <inheritdoc/>
    public void SetProfileOrder(IReadOnlyList<string> profileIds)
    {
        ArgumentNullException.ThrowIfNull(profileIds);

        var profiles = LoadProfilesFromCache();
        var requestedOrder = profileIds
            .Select((id, index) => (id, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.id))
            .GroupBy(item => item.id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

        var ordered = profiles
            .OrderBy(profile => requestedOrder.TryGetValue(profile.Id, out var index) ? index : int.MaxValue)
            .ThenBy(profile => profiles.IndexOf(profile))
            .ToList();

        SaveProfilesToCache(ordered);
        RaiseProfilesChanged();
    }

    private void EnsureProfileStorageUpgraded()
    {
        if (_profileFolderMigrationAttempted)
            return;

        _profileFolderMigrationAttempted = true;

        try
        {
            var profiles = LoadProfilesFromCache();

            bool changed = false;
            foreach (var profile in profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Id) || !Guid.TryParse(profile.Id, out _))
                {
                    profile.Id = Guid.NewGuid().ToString();
                    changed = true;
                    Logger.Info("Profile", $"Assigned missing profile ID for '{profile.Name}': {profile.Id}");
                }
                LauncherUtilities.GetProfileFolderPath(_appDir, profile, createIfMissing: false, migrateLegacyByName: true);
            }

            ProfileMigration.MigrateUnresolvedFolders(GetProfilesFolder(), profiles, _appDir);

            if (ProfileMigration.MigrateOrphanedFolders(GetProfilesFolder(), profiles))
                changed = true;

            if (changed)
                SaveProfilesToCache(profiles);
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Profile storage migration check failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Profile? CreateProfile(string name, string uuid, bool isOfficial = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uuid))
            {
                Logger.Warning("Profile", $"Cannot create profile with empty name or UUID");
                return null;
            }

            var trimmedName = name.Trim();
            if (trimmedName.Length < 1 || trimmedName.Length > 16)
            {
                Logger.Warning("Profile", $"Invalid name length: {trimmedName.Length} (must be 1-16 chars)");
                return null;
            }

            if (!Guid.TryParse(uuid.Trim(), out var parsedUuid))
            {
                Logger.Warning("Profile", $"Invalid UUID format: {uuid}");
                return null;
            }

            var profile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                UUID = parsedUuid.ToString(),
                Name = trimmedName,
                IsOfficial = isOfficial,
                CreatedAt = DateTime.UtcNow
            };

            var profiles = LoadProfilesFromCache();
            profiles.Add(profile);

            var config = _configStore.Configuration;
            if (profiles.Count == 1 || string.IsNullOrEmpty(config.SelectedProfileId))
            {
                config.SelectedProfileId = profile.Id;
                Logger.Info("Profile", $"Auto-activated new profile '{profile.Name}'");
            }

            SaveProfilesToCache(profiles);
            _configStore.SaveConfig();
            Logger.Info("Profile", $"Profile added to list. Total profiles: {profiles.Count}");
            Logger.Info("Profile", $"Config saved to disk");

            SaveProfileToDisk(profile);

            Logger.Success("Profile", $"Created profile '{trimmedName}' with UUID {parsedUuid}");
            RaiseProfilesChanged();
            return profile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to create profile: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public bool DeleteProfile(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return false;

            var profiles = LoadProfilesFromCache();
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            profiles.Remove(profile);
            SaveProfilesToCache(profiles);

            var config = _configStore.Configuration;
            if (config.SelectedProfileId == profileId)
            {
                config.SelectedProfileId = "";
            }
            _configStore.SaveConfig();

            DeleteProfileFromDisk(profileId, profile.Name);

            Logger.Success("Profile", $"Deleted profile '{profile.Name}'");
            RaiseProfilesChanged();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to delete profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool SwitchProfile(string profileId)
    {
        try
        {
            var profiles = LoadProfilesFromCache();
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            var currentUuid = _identity.GetCurrentUuid();
            if (!string.IsNullOrWhiteSpace(currentUuid))
                _skins.BackupProfileSkinData(currentUuid);

            _skins.RestoreProfileSkinData(profile);

            var config = _configStore.Configuration;
            config.SelectedProfileId = profile.Id;

            if (profile.IsOfficial)
            {
                config.AuthDomain = "sessions.hytale.com";
                Logger.Info("Profile", "Official profile selected: auth domain switched to sessions.hytale.com");
            }
            else if (config.AuthDomain == "sessions.hytale.com")
            {
                config.AuthDomain = "";
                Logger.Info("Profile", "Non-official profile selected: cleared official auth domain");
            }

            EnsureInstanceModsDirectory(profile);
            _configStore.SaveConfig();

            Logger.Success("Profile", $"Switched to profile '{profile.Name}'");
            RaiseProfilesChanged();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to switch profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool UpdateProfile(string profileId, string? newName, string? newUuid)
    {
        try
        {
            var profiles = LoadProfilesFromCache();
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            if (!string.IsNullOrWhiteSpace(newName))
                profile.Name = newName.Trim();

            if (!string.IsNullOrWhiteSpace(newUuid) && Guid.TryParse(newUuid.Trim(), out var parsedUuid))
                profile.UUID = parsedUuid.ToString();

            SaveProfilesToCache(profiles);

            UpdateProfileOnDisk(profile);

            Logger.Success("Profile", $"Updated profile '{profile.Name}'");
            RaiseProfilesChanged();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to update profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool RecordPlayTime(string profileId, long elapsedSeconds)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId) || elapsedSeconds <= 0)
            {
                return false;
            }

            var profiles = LoadProfilesFromCache();
            var profile = profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, profileId, StringComparison.Ordinal));
            if (profile is null)
                return false;

            profile.TotalPlaytime += TimeSpan.FromSeconds(elapsedSeconds);
            SaveProfilesToCache(profiles);
            RaiseProfilesChanged();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to record profile play time: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public Profile? DuplicateProfile(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                Logger.Warning("Profile", "Cannot duplicate profile with empty ID");
                return null;
            }

            var allProfiles = LoadProfilesFromCache();
            var sourceProfile = allProfiles.FirstOrDefault(p => p.Id == profileId);
            if (sourceProfile == null)
            {
                Logger.Warning("Profile", $"Profile not found: {profileId}");
                return null;
            }

            var newUuid = Guid.NewGuid().ToString();
            var newName = $"{sourceProfile.Name} Copy";
            int copyCount = 1;
            while (allProfiles.Any(p => p.Name == newName)) { copyCount++; newName = $"{sourceProfile.Name} Copy {copyCount}"; }

            var newProfile = new Profile { Id = Guid.NewGuid().ToString(), UUID = newUuid, Name = newName, CreatedAt = DateTime.UtcNow };
            allProfiles.Add(newProfile);
            SaveProfilesToCache(allProfiles);
            SaveProfileToDisk(newProfile);

            try
            {
                var sourceModsPath = GetProfileModsFolder(sourceProfile);
                var destModsPath = GetProfileModsFolder(newProfile);

                if (Directory.Exists(sourceModsPath))
                {
                    LauncherUtilities.CopyDirectory(sourceModsPath, destModsPath);
                    Logger.Info("Profile", $"Copied mods from '{sourceProfile.Name}' to '{newProfile.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy mods during duplication: {ex.Message}");
            }

            try
            {
                var versionPath = TryGetCurrentExistingInstancePath();
                if (string.IsNullOrWhiteSpace(versionPath))
                {
                    Logger.Info("Profile", "No existing instance selected, skipping UserData copy during duplication");
                }
                else
                {
                    var userDataPath = _instances.GetInstanceUserDataPath(versionPath);

                    if (Directory.Exists(userDataPath))
                    {
                        var sourceProfileFolder = LauncherUtilities.GetProfileFolderPath(_appDir, sourceProfile);
                        var sourceUserDataBackup = Path.Combine(sourceProfileFolder, "UserData");
                        var destProfileFolder = LauncherUtilities.GetProfileFolderPath(_appDir, newProfile);
                        var destUserDataBackup = Path.Combine(destProfileFolder, "UserData");

                        if (Directory.Exists(sourceUserDataBackup))
                        {
                            LauncherUtilities.CopyDirectory(sourceUserDataBackup, destUserDataBackup);
                            Logger.Info("Profile", $"Copied UserData from '{sourceProfile.Name}' to '{newProfile.Name}'");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy UserData during duplication: {ex.Message}");
            }

            try
            {
                var sourceProfileDir = LauncherUtilities.GetProfileFolderPath(_appDir, sourceProfile);
                var destProfileDir = LauncherUtilities.GetProfileFolderPath(_appDir, newProfile);

                var sourceSkin = Path.Combine(sourceProfileDir, "skin.png");
                if (File.Exists(sourceSkin))
                {
                    File.Copy(sourceSkin, Path.Combine(destProfileDir, "skin.png"), true);
                }

                var sourceAvatar = Path.Combine(sourceProfileDir, "avatar.png");
                if (File.Exists(sourceAvatar))
                {
                    File.Copy(sourceAvatar, Path.Combine(destProfileDir, "avatar.png"), true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy skin/avatar during duplication: {ex.Message}");
            }

            Logger.Success("Profile", $"Duplicated profile '{sourceProfile.Name}' → '{newProfile.Name}'");
            RaiseProfilesChanged();
            return newProfile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to duplicate profile: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public Profile? DuplicateProfileWithoutData(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                Logger.Warning("Profile", "Cannot duplicate profile with empty ID");
                return null;
            }

            var allProfiles = LoadProfilesFromCache();
            var sourceProfile = allProfiles.FirstOrDefault(p => p.Id == profileId);
            if (sourceProfile == null)
            {
                Logger.Warning("Profile", $"Profile not found: {profileId}");
                return null;
            }

            var newUuid = Guid.NewGuid().ToString();
            var newName = $"{sourceProfile.Name} Copy";
            int copyCount = 1;
            while (allProfiles.Any(p => p.Name == newName)) { copyCount++; newName = $"{sourceProfile.Name} Copy {copyCount}"; }

            var newProfile = new Profile { Id = Guid.NewGuid().ToString(), UUID = newUuid, Name = newName, CreatedAt = DateTime.UtcNow };
            allProfiles.Add(newProfile);
            SaveProfilesToCache(allProfiles);

            SaveProfileToDisk(newProfile);

            try
            {
                var sourceModsPath = GetProfileModsFolder(sourceProfile);
                var destModsPath = GetProfileModsFolder(newProfile);

                if (Directory.Exists(sourceModsPath))
                {
                    LauncherUtilities.CopyDirectory(sourceModsPath, destModsPath);
                    Logger.Info("Profile", $"Copied mods from '{sourceProfile.Name}' to '{newProfile.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy mods during duplication: {ex.Message}");
            }

            try
            {
                var sourceProfileDir = LauncherUtilities.GetProfileFolderPath(_appDir, sourceProfile);
                var destProfileDir = LauncherUtilities.GetProfileFolderPath(_appDir, newProfile);

                var sourceSkin = Path.Combine(sourceProfileDir, "skin.png");
                if (File.Exists(sourceSkin))
                {
                    File.Copy(sourceSkin, Path.Combine(destProfileDir, "skin.png"), true);
                }

                var sourceAvatar = Path.Combine(sourceProfileDir, "avatar.png");
                if (File.Exists(sourceAvatar))
                {
                    File.Copy(sourceAvatar, Path.Combine(destProfileDir, "avatar.png"), true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy skin/avatar during duplication: {ex.Message}");
            }

            Logger.Success("Profile", $"Duplicated profile (without UserData) '{sourceProfile.Name}' → '{newProfile.Name}'");
            RaiseProfilesChanged();
            return newProfile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to duplicate profile without data: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public string? GetCurrentProfileFolder()
    {
        try
        {
            var profile = GetSelectedProfile();

            if (profile == null)
            {
                Logger.Warning("Profile", "No active profile folder is available");
                return null;
            }
            var profileDir = LauncherUtilities.GetProfileFolderPath(
                _appDir,
                profile,
                createIfMissing: false);

            if (!Directory.Exists(profileDir))
            {
                Directory.CreateDirectory(profileDir);
                Logger.Info("Profile", $"Created profile folder: {profileDir}");

                try
                {
                    var profileInfo = new
                    {
                        Username = profile.Name,
                        Uuid = profile.UUID,
                        CreatedAt = DateTime.UtcNow.ToString("o")
                    };
                    var infoPath = LauncherJsonFile.GetPath(profileDir, "Profile.json", "profile.json");
                    var json = JsonSerializer.Serialize(profileInfo, JsonDefaults.IndentedUnsafeRelaxed);
                    File.WriteAllText(infoPath, json);
                    Logger.Info("Profile", $"Created profile info file: {infoPath}");
                }
                catch (Exception infoEx)
                {
                    Logger.Warning("Profile", $"Failed to write profile info: {infoEx.Message}");
                }
            }

            return profileDir;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to resolve profile folder: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public void InitializeProfileModsSymlink()
    {
        try
        {
            var profile = GetSelectedProfile();
            if (profile == null)
            {
                Logger.Info("Mods", "No active profile, ensuring instance mods directory without profile linking");
                EnsureInstanceModsDirectory(null);
                return;
            }

            // Older builds linked Mods into profile folders; migrate those links back into UserData
            EnsureInstanceModsDirectory(profile);
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to initialize instance mods directory: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void MigrateLegacyModsLinks()
    {
        try
        {
            foreach (var instance in _instances.GetInstalledInstances())
                EnsureInstanceModsDirectory(null, instance.Path);

            // Preserve the old selected-instance fallback for unfinished instances that
            // cannot yet be returned by GetInstalledInstances.
            InitializeProfileModsSymlink();
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to migrate legacy profile mod links: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GetProfilesFolder()
    {
        return LauncherUtilities.GetProfilesRoot(_appDir);
    }

    #region Private Helper Methods

    /// <summary>
    /// Gets the path to a profile's mods folder
    /// </summary>
    private string GetProfileModsFolder(Profile profile)
    {
        var profileDir = LauncherUtilities.GetProfileFolderPath(_appDir, profile);
        var modsDir = Path.Combine(profileDir, "Mods");
        Directory.CreateDirectory(modsDir);
        return modsDir;
    }

    /// <summary>
    /// Ensures the active instance has a real UserData/Mods directory.
    /// If a legacy profile symlink/junction is detected, migrates files back
    /// </summary>
    private void EnsureInstanceModsDirectory(Profile? profile, string? instancePath = null)
    {
        try
        {
            var versionPath = instancePath ?? TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                Logger.Info("Mods", "No existing instance found for mods directory initialization");
                return;
            }

            var userDataPath = Path.Combine(versionPath, "UserData");
            var gameModsPath = Path.Combine(userDataPath, "Mods");

            Directory.CreateDirectory(userDataPath);

            if (File.Exists(gameModsPath))
            {
                Logger.Warning("Mods",
                    $"Found a file where the Mods directory should be ({gameModsPath}), removing it");
                File.Delete(gameModsPath);
            }

            if (!Directory.Exists(gameModsPath))
            {
                Directory.CreateDirectory(gameModsPath);
                return;
            }

            var dirInfo = new DirectoryInfo(gameModsPath);
            bool isSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
            if (!isSymlink)
            {
                return;
            }

            string? targetPath = null;
            try
            {
                targetPath = dirInfo.ResolveLinkTarget(true)?.FullName;
            }
            catch
            {
                // Junction target resolution is platform-dependent
            }

            var migrationSources = new List<string>();
            if (!string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath))
            {
                migrationSources.Add(targetPath);
            }

            if (profile != null)
            {
                var profileModsPath = GetProfileModsFolder(profile);
                if (Directory.Exists(profileModsPath) &&
                    !migrationSources.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(profileModsPath), StringComparison.OrdinalIgnoreCase)))
                {
                    migrationSources.Add(profileModsPath);
                }
            }

            Logger.Info("Mods", "Legacy profile mods link detected, migrating back to instance UserData/Mods");

            try
            {
                // Deleting a reparse point without recursion removes the link itself.
                // Never recurse here: on platforms that follow a junction it could
                // delete the profile directory that contains the user's mod files.
                Directory.Delete(gameModsPath, false);
            }
            catch (Exception ex)
            {
                Logger.Warning("Mods", $"Could not remove legacy mods link '{gameModsPath}': {ex.Message}");
                return;
            }

            Directory.CreateDirectory(gameModsPath);

            foreach (var source in migrationSources)
                if (Directory.Exists(source))
                {
                    foreach (var file in Directory.GetFiles(source))
                    {
                        var destFile = Path.Combine(gameModsPath, Path.GetFileName(file));
                        File.Copy(file, destFile, true);
                    }
                }

            Logger.Success("Mods", $"Using instance-local mods directory: {gameModsPath}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to ensure instance mods directory: {ex.Message}");
        }
    }

    private string? TryGetCurrentExistingInstancePath()
    {
        var selected = _instances.GetSelectedInstance();
        if (selected != null)
        {
            var path = _instances.GetInstancePathById(selected.Id);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        // Profile data remains instance-scoped when no instance is selected
        return _instances.GetInstalledInstances().FirstOrDefault()?.Path;
    }

    /// <summary>
    /// Saves a profile to disk as a .sh file with name and UUID, plus avatar if available
    /// </summary>
    private void SaveProfileToDisk(Profile profile)
    {
        try
        {
            var profileDir = LauncherUtilities.GetProfileFolderPath(_appDir, profile);
            Directory.CreateDirectory(profileDir);

            var modsDir = Path.Combine(profileDir, "Mods");
            Directory.CreateDirectory(modsDir);

            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# Hyprism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by Hyprism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);

            _skins.CopyProfileSkinData(profile.UUID, profileDir);

            Logger.Info("Profile", $"Saved profile to disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to save profile to disk: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates a profile's disk files when it's modified
    /// </summary>
    private void UpdateProfileOnDisk(Profile profile)
    {
        try
        {
            var profileDir = LauncherUtilities.GetProfileFolderPath(_appDir, profile);

            if (!Directory.Exists(profileDir))
            {
                SaveProfileToDisk(profile);
                return;
            }

            foreach (var oldSh in Directory.GetFiles(profileDir, "*.sh"))
            {
                File.Delete(oldSh);
            }

            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# Hyprism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}
# Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by Hyprism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);

            Logger.Info("Profile", $"Updated profile on disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to update profile on disk: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a profile's disk folder
    /// </summary>
    private void DeleteProfileFromDisk(string profileId, string? profileName = null)
    {
        try
        {
            var profilesDir = GetProfilesFolder();

            if (!string.IsNullOrEmpty(profileName))
            {
                var safeName = LauncherUtilities.SanitizeFileName(profileName);
                var profileDirByName = Path.Combine(profilesDir, safeName);
                if (Directory.Exists(profileDirByName))
                {
                    Directory.Delete(profileDirByName, true);
                    Logger.Info("Profile", $"Deleted profile from disk: {profileDirByName}");
                }
            }

            var profileDir = Path.Combine(profilesDir, profileId);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, true);
                Logger.Info("Profile", $"Deleted profile from disk: {profileDir}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to delete profile from disk: {ex.Message}");
        }
    }

    #endregion
}
