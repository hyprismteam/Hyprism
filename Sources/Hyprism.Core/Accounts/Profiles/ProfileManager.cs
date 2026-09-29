// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Encodings.Web;
using System.Text.Json;
using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Assets;

namespace Hyprism.Core.Accounts;

/// <summary>
/// Manages user profiles, avatars, nicknames, and UUIDs.
/// </summary>
public class ProfileManager : IProfileManager
{
    private readonly string _appDataPath;
    private readonly IConfigStore _configStore;
    private readonly IAvatarCache? _avatars;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileManager"/> class.
    /// </summary>
    /// <param name="appDataPath">The application data directory path.</param>
    /// <param name="configStore">The configuration service for accessing user settings.</param>
    /// <param name="avatars">The avatar service for CachedAvatarPreviews lookups.</param>
    public ProfileManager(string appDataPath, IConfigStore configStore, IAvatarCache? avatars = null)
    {
        _appDataPath = appDataPath;
        _configStore = configStore;
        _avatars = avatars;
    }

    /// <inheritdoc/>
    public event Action? ProfilesChanged;

    private void RaiseProfilesChanged() => ProfilesChanged?.Invoke();

    /// <inheritdoc/>
    public string GetNick()
        => GetActiveProfileField(profile => profile.Name) ?? string.Empty;

    /// <inheritdoc/>
    public bool SetNick(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick) || nick.Length > 16)
            return false;

        return UpdateActiveProfileField(profile => profile.Name = nick);
    }

    /// <inheritdoc/>
    public string GetUUID() => GetCurrentUuid();

    /// <inheritdoc/>
    public bool SetUUID(string uuid)
    {
        return Guid.TryParse(uuid, out var parsed)
               && UpdateActiveProfileField(profile => profile.UUID = parsed.ToString());
    }

    /// <inheritdoc/>
    public string GetCurrentUuid()
        => GetActiveProfileField(profile => profile.UUID) ?? string.Empty;

    /// <inheritdoc/>
    public string GenerateNewUuid()
    {
        return Guid.NewGuid().ToString();
    }

    /// <inheritdoc/>
    public string? GetAvatarPreview()
    {
        var uuid = GetCurrentUuid();
        return GetAvatarPreviewForUUID(uuid);
    }

    /// <inheritdoc/>
    public string? GetAvatarPreviewForUUID(string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid))
                return null;

            var profile = ReadProfilesFromCache().FirstOrDefault(p => p.UUID == uuid);
            if (profile != null)
            {
                var profileDir = LauncherUtilities.GetProfileFolderPath(_appDataPath, profile);
                var profileAvatarPath = Path.Combine(profileDir, "avatar.png");

                if (File.Exists(profileAvatarPath) && new FileInfo(profileAvatarPath).Length > 100)
                {
                    var bytes = File.ReadAllBytes(profileAvatarPath);
                    return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }
            }

            if (_avatars != null)
            {
                var backupPath = _avatars.GetAvatarBackupPath(uuid);
                if (File.Exists(backupPath) && new FileInfo(backupPath).Length > 100)
                {
                    var bytes = File.ReadAllBytes(backupPath);
                    CopyAvatarToProfile(profile, bytes);
                    return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }

                if (_avatars.BackupAvatar(uuid))
                {
                    var freshBackupPath = _avatars.GetAvatarBackupPath(uuid);
                    if (File.Exists(freshBackupPath) && new FileInfo(freshBackupPath).Length > 100)
                    {
                        var bytes = File.ReadAllBytes(freshBackupPath);
                        CopyAvatarToProfile(profile, bytes);
                        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                    }
                }
            }

            var skinsPath = Path.Combine(_appDataPath, "skins", uuid);
            if (Directory.Exists(skinsPath))
            {
                var pngPath = Path.Combine(skinsPath, "skin.png");
                var jpgPath = Path.Combine(skinsPath, "skin.jpg");
                string? skinPath = File.Exists(pngPath) ? pngPath : File.Exists(jpgPath) ? jpgPath : null;
                if (skinPath != null)
                {
                    var bytes = File.ReadAllBytes(skinPath);
                    var mime = skinPath.EndsWith(".png") ? "image/png" : "image/jpeg";
                    return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("Avatar", $"Could not load avatar preview for {uuid}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copies avatar bytes to the profile's root folder for persistent backup.
    /// </summary>
    private void CopyAvatarToProfile(Profile? profile, byte[] avatarBytes)
    {
        if (profile == null) return;
        try
        {
            var profileDir = LauncherUtilities.GetProfileFolderPath(_appDataPath, profile);
            Directory.CreateDirectory(profileDir);
            File.WriteAllBytes(Path.Combine(profileDir, "avatar.png"), avatarBytes);
        }
        catch { }
    }

    /// <inheritdoc/>
    public bool ClearAvatarCache()
    {
        try
        {
            var skinsPath = Path.Combine(_appDataPath, "skins");
            if (Directory.Exists(skinsPath))
            {
                Directory.Delete(skinsPath, true);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public string GetAvatarDirectory()
    {
        var uuid = GetCurrentUuid();
        var skinsPath = Path.Combine(_appDataPath, "skins", uuid);

        if (!Directory.Exists(skinsPath))
            Directory.CreateDirectory(skinsPath);

        return skinsPath;
    }


    private string GetProfileCachePath()
    {
        var root = LauncherUtilities.GetProfilesRoot(_appDataPath);
        Directory.CreateDirectory(root);
        return LauncherJsonFile.GetPath(root, "Profiles.json", "profiles.json");
    }

    private List<Profile> ReadProfilesFromCache()
    {
        var path = GetProfileCachePath();
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(path), JsonOpts) ?? []; }
        catch { return []; }
    }

    private void WriteProfilesToCache(List<Profile> profiles)
    {
        try { File.WriteAllText(GetProfileCachePath(), JsonSerializer.Serialize(profiles, JsonOpts)); }
        catch { }
    }

    /// <summary>Gets a field value from the currently selected profile, or null if none is active</summary>
    private string? GetActiveProfileField(Func<Profile, string?> selector)
    {
        var id = _configStore.Configuration.SelectedProfileId;
        if (string.IsNullOrEmpty(id)) return null;
        return selector(ReadProfilesFromCache().FirstOrDefault(p => p.Id == id) ?? new Profile());
    }

    /// <summary>Mutates the currently selected profile in the cache</summary>
    private bool UpdateActiveProfileField(Action<Profile> mutate)
    {
        var id = _configStore.Configuration.SelectedProfileId;
        if (string.IsNullOrEmpty(id)) return false;
        var profiles = ReadProfilesFromCache();
        var profile = profiles.FirstOrDefault(p => p.Id == id);
        if (profile == null) return false;
        mutate(profile);
        WriteProfilesToCache(profiles);
        RaiseProfilesChanged();
        return true;
    }


    /// <inheritdoc/>
    public List<Profile> GetProfiles() => ReadProfilesFromCache();

    /// <inheritdoc/>
    public bool CreateProfile(string name, string? uuid = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 16)
            return false;
        var profileUuid = uuid ?? GenerateNewUuid();
        if (!Guid.TryParse(profileUuid, out var parsedUuid))
            return false;

        var profiles = ReadProfilesFromCache();
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim(),
            UUID = parsedUuid.ToString(),
            CreatedAt = DateTime.UtcNow
        };
        profiles.Add(profile);
        WriteProfilesToCache(profiles);
        if (string.IsNullOrWhiteSpace(_configStore.Configuration.SelectedProfileId))
        {
            _configStore.Configuration.SelectedProfileId = profile.Id;
            _configStore.SaveConfig();
        }
        RaiseProfilesChanged();
        return true;
    }

    /// <inheritdoc/>
    public bool DeleteProfile(string profileId)
    {
        var profiles = ReadProfilesFromCache();
        var profile = profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return false;
        profiles.Remove(profile);
        WriteProfilesToCache(profiles);
        if (_configStore.Configuration.SelectedProfileId == profileId)
        {
            _configStore.Configuration.SelectedProfileId = profiles.FirstOrDefault()?.Id ?? string.Empty;
            _configStore.SaveConfig();
        }
        RaiseProfilesChanged();
        return true;
    }

    /// <inheritdoc/>
    public bool SwitchProfile(string profileId)
    {
        var profile = ReadProfilesFromCache().FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return false;
        _configStore.Configuration.SelectedProfileId = profile.Id;
        _configStore.SaveConfig();
        RaiseProfilesChanged();
        return true;
    }

    /// <inheritdoc/>
    public string GetProfilePath(Profile profile) => LauncherUtilities.GetProfileFolderPath(_appDataPath, profile);
}
