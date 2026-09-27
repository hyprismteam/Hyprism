// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Models;

namespace Hyprism.Core.Accounts;

/// <summary>
/// Provides functionality for managing user profiles including creation, deletion, switching, and data persistence
/// </summary>
public interface IProfileRepository
{
    /// <summary>
    /// Raised after the profile collection or the active profile changes.
    /// Subscribers should re-read <see cref="GetProfiles"/> and
    /// <see cref="GetSelectedProfileId"/> for the new state
    /// </summary>
    event Action? ProfilesChanged;

    /// <summary>
    /// Gets all available user profiles
    /// </summary>
    /// <remarks>Filters out profiles with missing names or UUIDs</remarks>
    /// <returns>A list of all user profiles with valid names and UUIDs</returns>
    List<Profile> GetProfiles();

    /// <summary>
    /// Persists the display order of saved profiles
    /// </summary>
    /// <param name="profileIds">Profile identifiers in the requested order</param>
    void SetProfileOrder(IReadOnlyList<string> profileIds);

    /// <summary>
    /// Gets the ID of the currently active profile
    /// </summary>
    /// <returns>The profile ID string, or empty if no profile is selected</returns>
    string GetSelectedProfileId();

    /// <summary>
    /// Gets the currently active profile object
    /// </summary>
    /// <returns>The active <see cref="Profile"/>, or null if none is selected</returns>
    Profile? GetSelectedProfile();

    /// <summary>
    /// Creates a new profile with the specified name, UUID, and account type
    /// </summary>
    /// <param name="name">The profile name (1-16 characters)</param>
    /// <param name="uuid">The UUID for the profile</param>
    /// <param name="isOfficial">Whether the profile is linked to an official Hytale account</param>
    /// <remarks>Validates the name length and UUID format before creation</remarks>
    /// <returns>The created profile, or null if creation failed</returns>
    Profile? CreateProfile(string name, string uuid, bool isOfficial = false);

    /// <summary>
    /// Deletes a profile by its unique identifier
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to delete</param>
    /// <remarks>Updates <see cref="GetSelectedProfileId"/> if the deleted profile was active</remarks>
    /// <returns>True if the profile was successfully deleted; otherwise, false</returns>
    bool DeleteProfile(string profileId);

    /// <summary>
    /// Switches to a profile by its unique ID
    /// </summary>
    /// <param name="profileId">The profile ID to switch to</param>
    /// <remarks>Backs up the current profile skin data and restores the selected profile skin data</remarks>
    /// <returns>True if the switch was successful; otherwise, false</returns>
    bool SwitchProfile(string profileId);

    /// <summary>
    /// Updates an existing profile with new name and/or UUID
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to update</param>
    /// <param name="newName">The new name for the profile, or null to keep existing</param>
    /// <param name="newUuid">The new UUID for the profile, or null to keep existing</param>
    /// <returns>True if the update was successful; otherwise, false</returns>
    bool UpdateProfile(string profileId, string? newName, string? newUuid);

    /// <summary>
    /// Adds tracked game time to a profile
    /// </summary>
    /// <param name="profileId">The profile used for the game session</param>
    /// <param name="elapsedSeconds">The positive session duration in seconds</param>
    /// <returns>True when the statistics were persisted; otherwise, false</returns>
    bool RecordPlayTime(string profileId, long elapsedSeconds);

    /// <summary>
    /// Duplicates an existing profile including all user data (mods, UserData folder)
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to duplicate</param>
    /// <remarks>Copies the UserData folder, mods folder, and skin data from the source profile</remarks>
    /// <returns>The newly created profile, or null if duplication failed</returns>
    Profile? DuplicateProfile(string profileId);

    /// <summary>
    /// Duplicates an existing profile without copying user data (only profile settings)
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to duplicate</param>
    /// <remarks>Copies mods and skin/avatar data but not the UserData folder</remarks>
    /// <returns>The newly created profile, or null if duplication failed</returns>
    Profile? DuplicateProfileWithoutData(string profileId);

    /// <summary>
    /// Resolves the current profile folder and creates its metadata when needed
    /// </summary>
    /// <returns>The absolute profile folder path, or <see langword="null"/> when no profile is available</returns>
    string? GetCurrentProfileFolder();

    /// <summary>
    /// Initializes compatibility handling for profile mods storage.
    /// Ensures instance-local <c>UserData/Mods</c> exists and migrates legacy profile links when detected
    /// </summary>
    void InitializeProfileModsSymlink();

    /// <summary>
    /// Repairs legacy profile-backed mod links for every discovered instance.
    /// This is intended for startup migrations and is safe to repeat
    /// </summary>
    void MigrateLegacyModsLinks();

    /// <summary>
    /// Gets the path to the profiles root folder
    /// </summary>
    /// <returns>The absolute path to the profiles folder</returns>
    string GetProfilesFolder();
}
