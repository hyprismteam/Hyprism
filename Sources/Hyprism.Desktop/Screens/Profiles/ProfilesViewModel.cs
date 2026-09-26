// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Core.Accounts;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Models;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;

namespace Hyprism.Desktop.Screens.Profiles;

/// <summary>
/// Provides the native profile manager page and its profile creation flows.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private static readonly Regex OfflineNamePattern = new(
        "^[a-zA-Z0-9_-]{3,16}$",
        RegexOptions.CultureInvariant);

    private readonly IProfileManager _profileManager;
    private readonly IProfileRepository _profileRepository;
    private readonly IInstanceRepository? _instanceRepository;
    private readonly IHytaleAuthenticator? _authenticator;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly StringLocalizer _localizer;
    private CancellationTokenSource? _authenticationCancellation;
    private bool _isAuthenticationCancellationArmed;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(IsProfileEditorVisible))]
    [NotifyPropertyChangedFor(nameof(CanActivateSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private ProfileItemViewModel? _selectedProfile;

    private ProfileCreationStep _creationStep;

    [ObservableProperty]
    private bool _isCreationVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticationCancellationArmed))]
    private bool _isAuthenticating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateOfflineProfile))]
    private string _offlineProfileName = GenerateDefaultOfflineName();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditNameInvalid))]
    [NotifyPropertyChangedFor(nameof(EditNameErrorMessage))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditUuidInvalid))]
    [NotifyPropertyChangedFor(nameof(EditUuidErrorMessage))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private string _editUuid = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditNameInvalid))]
    [NotifyPropertyChangedFor(nameof(IsEditUuidInvalid))]
    [NotifyPropertyChangedFor(nameof(EditNameErrorMessage))]
    [NotifyPropertyChangedFor(nameof(EditUuidErrorMessage))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isProfileEditMounted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingProfileDeletion))]
    private ProfileItemViewModel? _pendingProfileDeletion;

    [ObservableProperty]
    private bool _isProfileDeletionOpen;

    public ProfilesViewModel(
        IProfileManager profileManager,
        IProfileRepository profileRepository,
        IExternalUriLauncher uriLauncher,
        StringLocalizer localizer,
        IHytaleAuthenticator? authenticator = null,
        IInstanceRepository? instanceRepository = null)
    {
        _profileManager = profileManager;
        _profileRepository = profileRepository;
        _uriLauncher = uriLauncher;
        _localizer = localizer;
        _authenticator = authenticator;
        _instanceRepository = instanceRepository;
        _profileRepository.ProfilesChanged += OnProfilesChanged;
        _profileManager.ProfilesChanged += OnProfilesChanged;

        RefreshProfiles();
    }

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    public event EventHandler<ActiveProfileChangedEventArgs>? ActiveProfileChanged;

    public ProfileItemViewModel? ActiveProfile => Profiles.FirstOrDefault(profile => profile.IsActive);

    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool HasProfiles => Profiles.Count > 0;
    public bool HasNoProfiles => !HasProfiles;
    public bool IsEmptyStateVisible => HasNoProfiles;
    public bool IsProfileEditorVisible => SelectedProfile is not null;
    public bool CanCreateOfflineProfile => OfflineNamePattern.IsMatch(OfflineProfileName.Trim());
    public bool IsEditNameInvalid => IsEditing && GetEditNameErrorKey() is not null;
    public bool IsEditUuidInvalid => IsEditing && GetEditUuidErrorKey() is not null;
    public string EditNameErrorMessage => GetEditErrorMessage(GetEditNameErrorKey());
    public string EditUuidErrorMessage => GetEditErrorMessage(GetEditUuidErrorKey());
    public bool CanSaveProfile => IsEditing && SelectedProfile is { IsOfficial: false }
        && !IsEditNameInvalid && !IsEditUuidInvalid;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasPendingProfileDeletion => PendingProfileDeletion is not null;
    public bool CanActivateSelectedProfile => SelectedProfile is { IsActive: false };
    public bool IsCreateChoiceVisible => _creationStep is ProfileCreationStep.ChooseType;
    public bool IsOfflineCreationVisible => _creationStep is ProfileCreationStep.Offline;
    public bool IsOfficialCreationVisible => _creationStep is ProfileCreationStep.Official;
    public bool IsAuthenticationCancellationArmed =>
        IsAuthenticating && _isAuthenticationCancellationArmed;

    private string GetEditErrorMessage(string? key)
        => IsEditing && key is not null ? _localizer[key] : string.Empty;

    private string? GetEditNameErrorKey()
    {
        var name = EditName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return "profileEditor.usernameRequired";
        if (name.Length < 3)
            return "profileEditor.usernameTooShort";
        if (name.Length > 16)
            return "profileEditor.usernameTooLong";
        return OfflineNamePattern.IsMatch(name) ? null : "profileEditor.usernameInvalidCharacters";
    }

    private string? GetEditUuidErrorKey()
    {
        var uuid = EditUuid?.Trim() ?? string.Empty;
        if (uuid.Length == 0)
            return "profileEditor.uuidRequired";
        return Guid.TryParse(uuid, out _) ? null : "profileEditor.uuidInvalidFormat";
    }

    public string SavedProfilesLabel => _localizer["profiles.savedProfiles"];
    public string EditorLabel => _localizer["profiles.editor"];
    public string PlayTimeLabel => _localizer["instances.info.playtime"];
    public string FavoriteInstanceLabel => _localizer["profiles.favoriteInstance"];
    public string CreateProfileLabel => _localizer["profiles.wizard.title"];
    public string CreateProfileHint => _localizer["profiles.wizard.chooseType"];
    public string OfflineProfileLabel => _localizer["profiles.wizard.unofficial"];
    public string OfflineProfileHint => _localizer["profiles.wizard.unofficialDesc"];
    public string OfficialProfileLabel => _localizer["profiles.wizard.official"];
    public string OfficialProfileHint => _localizer["profiles.wizard.officialDesc"];
    public string ProfileNameLabel => _localizer["profileEditor.username"];
    public string ProfileNameHint => _localizer["profileEditor.usernameHint"];
    public string UuidLabel => _localizer["profileEditor.uuid"];
    public string UuidHint => _localizer["profileEditor.uuidHint"];
    public string NamePlaceholder => _localizer["profiles.wizard.namePlaceholder"];
    public string CreateOfflineTitle => _localizer["profiles.wizard.nameTitle"];
    public string CreateOfflineHint => _localizer["profiles.wizard.nameDesc"];
    public string AuthenticationTitle => _localizer["profiles.wizard.authTitle"];
    public string AuthenticationHint => _localizer["profiles.wizard.authDesc"];
    public string BrowserHint => _localizer["profiles.wizard.browserHint"];
    public string SignInLabel => _localizer["profiles.wizard.loginHytale"];
    public string CreateLabel => _localizer["profiles.wizard.create"];
    public string AddLabel => _localizer["profiles.createNew"];
    public string CancelLabel => _localizer["common.cancel"];
    public string BackLabel => _localizer["common.back"];
    public string SaveLabel => _localizer["common.save"];
    public string EditLabel => _localizer["editor.action"];
    public string ProfileEditorTitle => _localizer["profileEditor.title"];
    public string CopyLabel => _localizer["profiles.copyUuid"];
    public string FolderLabel => _localizer["profiles.openFolder"];
    public string DeleteActionLabel => _localizer["common.delete"];
    public string ActivationLabel => _localizer["profiles.setActive"];
    public string ActiveLabel => _localizer["profiles.active"];
    public string DeleteLabel => _localizer["profiles.deleteProfile"];
    public string DuplicateLabel => _localizer["profiles.duplicateProfile"];
    public string RandomizeNameLabel => _localizer["profiles.generateName"];
    public string RandomizeUuidLabel => _localizer["profiles.randomUuid"];
    public string OfficialLockedLabel => _localizer["profiles.officialLocked"];
    public string NoProfilesLabel => _localizer["profiles.noProfiles"];
    public string DeleteTitle => _localizer["confirmation.title"];
    public string DeleteHint => _localizer["deleteProfile.cannotUndo"];
    public string OfflineNameRuleLabel => _localizer["profiles.wizard.nickRules"];

    private void OnProfilesChanged()
    {
        if (_disposed)
            return;

        // Mutations from view model commands arrive on the UI thread and observers
        // of ActiveProfileChanged expect the collection to be fresh right after the
        // command returns, so refresh synchronously there
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshProfiles();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
                RefreshProfiles();
        });
    }

    /// <summary>
    /// Reloads the saved profile list and retains the currently displayed item when possible.
    /// </summary>
    public void RefreshProfiles(string? preferredProfileId = null)
    {
        if (_disposed)
            return;

        var selectedId = preferredProfileId ?? SelectedProfile?.Id;
        List<Profile> profileList;
        try
        {
            profileList = _profileRepository.GetProfiles() ?? [];
        }
        catch
        {
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
            return;
        }

        foreach (var profile in Profiles)
            profile.Dispose();
        Profiles.Clear();

        var activeProfileId = _profileRepository.GetSelectedProfileId();
        foreach (var profile in profileList)
        {
            Profiles.Add(new ProfileItemViewModel(
                profile.Id,
                profile.Name,
                profile.UUID,
                profile.IsOfficial,
                string.Equals(profile.Id, activeProfileId, StringComparison.Ordinal),
                string.Equals(profile.Id, selectedId, StringComparison.Ordinal),
                profile.IsOfficial ? OfficialProfileLabel : OfflineProfileLabel,
                FormatPlayTime(profile.TotalPlaytime),
                ResolveFavoriteInstance(profile),
                LoadAvatar(profile.UUID)));
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasNoProfiles));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(ActiveProfile));

        if (IsCreationVisible)
            return;

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedId) ??
                          Profiles.FirstOrDefault(profile => profile.IsActive);
    }

    public void RefreshLocalization()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(SavedProfilesLabel), nameof(EditorLabel), nameof(CreateProfileLabel),
                     nameof(PlayTimeLabel), nameof(FavoriteInstanceLabel),
                     nameof(CreateProfileHint), nameof(OfflineProfileLabel), nameof(OfflineProfileHint),
                     nameof(OfficialProfileLabel), nameof(OfficialProfileHint), nameof(ProfileNameLabel),
                     nameof(ProfileNameHint), nameof(UuidLabel), nameof(UuidHint), nameof(NamePlaceholder),
                     nameof(EditNameErrorMessage), nameof(EditUuidErrorMessage),
                     nameof(CreateOfflineTitle), nameof(CreateOfflineHint), nameof(AuthenticationTitle),
                     nameof(AuthenticationHint), nameof(BrowserHint), nameof(SignInLabel),
                     nameof(CreateLabel), nameof(AddLabel),
                     nameof(CancelLabel), nameof(BackLabel), nameof(SaveLabel), nameof(EditLabel), nameof(ProfileEditorTitle),
                     nameof(CopyLabel), nameof(FolderLabel), nameof(DeleteActionLabel), nameof(ActivationLabel), nameof(ActiveLabel), nameof(DeleteLabel), nameof(DuplicateLabel),
                     nameof(RandomizeNameLabel), nameof(RandomizeUuidLabel), nameof(OfficialLockedLabel),
                     nameof(NoProfilesLabel), nameof(DeleteTitle), nameof(DeleteHint), nameof(OfflineNameRuleLabel)
                 })
        {
            OnPropertyChanged(propertyName);
        }

        RefreshProfiles(SelectedProfile?.Id);
    }

    public void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    [RelayCommand]
    private void SelectProfile(ProfileItemViewModel? profile)
    {
        if (profile is null)
            return;

        CloseProfileMenus();
        ClearStatus();
        IsCreationVisible = false;
        SetCreationStep(ProfileCreationStep.None);
        IsEditing = false;

        SelectedProfile = profile;
    }

    [RelayCommand]
    private void ActivateSelectedProfile()
    {
        var profile = SelectedProfile;
        if (profile is null || profile.IsActive)
            return;

        if (!_profileRepository.SwitchProfile(profile.Id))
        {
            SetStatus(_localizer["profiles.wizard.createFailed"], isError: true);
            RefreshProfiles(profile.Id);
            return;
        }

        _authenticator?.ReloadSessionForCurrentProfile();
        ClearStatus();
        ActiveProfileChanged?.Invoke(
            this,
            new ActiveProfileChangedEventArgs(profile.Name, profile.IsOfficial));
    }

    [RelayCommand]
    private void ShowCreateChoice()
    {
        CloseProfileMenus();
        ClearStatus();
        IsEditing = false;
        SetCreationStep(ProfileCreationStep.ChooseType);
        IsCreationVisible = true;
    }

    [RelayCommand]
    private void BeginOfflineCreation()
    {
        ClearStatus();
        OfflineProfileName = GenerateDefaultOfflineName();
        SetCreationStep(ProfileCreationStep.Offline);
    }

    [RelayCommand]
    private void BeginOfficialCreation()
    {
        ClearStatus();
        SetCreationStep(ProfileCreationStep.Official);
    }

    [RelayCommand]
    private void CancelCreation()
    {
        _authenticationCancellation?.Cancel();
        ClearStatus();
        IsCreationVisible = false;
    }

    internal void CompleteCreationTransition()
    {
        if (IsCreationVisible)
            return;

        SetCreationStep(ProfileCreationStep.None);
    }

    [RelayCommand]
    private void ReturnToCreationChoice()
    {
        _authenticationCancellation?.Cancel();
        ClearStatus();
        SetCreationStep(ProfileCreationStep.ChooseType);
    }

    [RelayCommand]
    private void GenerateOfflineProfileName()
        => OfflineProfileName = GenerateDefaultOfflineName();

    [RelayCommand]
    private void CreateOfflineProfile()
    {
        var name = OfflineProfileName.Trim();
        if (!OfflineNamePattern.IsMatch(name))
        {
            SetStatus(_localizer["profiles.wizard.nickInvalid"], isError: true);
            return;
        }

        var profile = _profileRepository.CreateProfile(name, Guid.NewGuid().ToString());
        if (profile is null || !_profileRepository.SwitchProfile(profile.Id))
        {
            SetStatus(_localizer["profiles.wizard.createFailed"], isError: true);
            return;
        }

        IsCreationVisible = false;
        ClearStatus();
        // Event-driven refreshes skip reselection while the wizard is open, so
        // reselect the created profile explicitly once it closes
        RefreshProfiles(profile.Id);
        ActiveProfileChanged?.Invoke(
            this,
            new ActiveProfileChangedEventArgs(profile.Name, profile.IsOfficial));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SignInWithHytaleAsync()
    {
        if (IsAuthenticating)
        {
            if (IsAuthenticationCancellationArmed)
                _authenticationCancellation?.Cancel();

            return;
        }

        if (_authenticator is null)
        {
            SetStatus(_localizer["profiles.wizard.authFailed"], isError: true);
            return;
        }

        using var authenticationCancellation = new CancellationTokenSource();
        _authenticationCancellation = authenticationCancellation;
        _isAuthenticationCancellationArmed = false;
        IsAuthenticating = true;
        ClearStatus();
        try
        {
            var session = await _authenticator.LoginAsync(
                _uriLauncher.LaunchAsync,
                authenticationCancellation.Token);
            if (authenticationCancellation.IsCancellationRequested)
                return;

            if (session is null)
            {
                SetStatus(_localizer["profiles.wizard.authFailed"], isError: true);
                return;
            }

            var identities = session.AccountProfiles.Count > 0
                ? session.AccountProfiles
                : [(session.Username, session.UUID)];
            var existingProfiles = _profileRepository.GetProfiles() ?? [];
            Profile? firstProfile = null;

            foreach (var (username, uuid) in identities)
            {
                if (string.IsNullOrWhiteSpace(username) || !Guid.TryParse(uuid, out _))
                    continue;

                var profile = existingProfiles.FirstOrDefault(existing =>
                                  existing.IsOfficial &&
                                  string.Equals(existing.UUID, uuid, StringComparison.OrdinalIgnoreCase)) ??
                              _profileRepository.CreateProfile(username, uuid, isOfficial: true);
                if (profile is null)
                    continue;

                _authenticator.SaveSessionToProfile(profile);
                firstProfile ??= profile;
            }

            if (firstProfile is null || !_profileRepository.SwitchProfile(firstProfile.Id))
            {
                SetStatus(_localizer["profiles.wizard.createFailed"], isError: true);
                return;
            }

            _authenticator.ReloadSessionForCurrentProfile();
            IsCreationVisible = false;
            // The authenticator may flip IsOfficial directly inside profiles.json,
            // so reload explicitly instead of relying on repository events alone
            RefreshProfiles(firstProfile.Id);
            ActiveProfileChanged?.Invoke(
                this,
                new ActiveProfileChangedEventArgs(firstProfile.Name, firstProfile.IsOfficial));
        }
        catch (OperationCanceledException) when (authenticationCancellation.IsCancellationRequested)
        {
            ClearStatus();
        }
        catch
        {
            SetStatus(_localizer["profiles.wizard.authError"], isError: true);
        }
        finally
        {
            if (ReferenceEquals(_authenticationCancellation, authenticationCancellation))
                _authenticationCancellation = null;

            _isAuthenticationCancellationArmed = false;
            IsAuthenticating = false;
        }
    }

    public void ArmAuthenticationCancellation()
    {
        if (!IsAuthenticating || _isAuthenticationCancellationArmed)
            return;

        _isAuthenticationCancellationArmed = true;
        OnPropertyChanged(nameof(IsAuthenticationCancellationArmed));
    }

    [RelayCommand]
    private void BeginEditing()
    {
        if (SelectedProfile is null || SelectedProfile.IsOfficial)
            return;

        ClearStatus();
        EditName = SelectedProfile.Name;
        EditUuid = SelectedProfile.Uuid;
        IsProfileEditMounted = true;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditProfile(ProfileItemViewModel? profile)
    {
        if (profile is null || profile.IsOfficial)
            return;

        SelectProfile(profile);
        BeginEditing();
    }

    [RelayCommand]
    private void CancelEditing()
    {
        if (SelectedProfile is not null)
        {
            EditName = SelectedProfile.Name;
            EditUuid = SelectedProfile.Uuid;
        }
        IsEditing = false;
        ClearStatus();
    }

    public void CompleteProfileEditClose()
        => IsProfileEditMounted = false;

    [RelayCommand]
    private void RandomizeEditName()
        => EditName = GenerateDefaultOfflineName();

    [RelayCommand]
    private void RandomizeEditUuid()
        => EditUuid = Guid.NewGuid().ToString();

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is null || SelectedProfile.IsOfficial)
            return;

        var name = EditName?.Trim() ?? string.Empty;
        var uuid = EditUuid?.Trim() ?? string.Empty;
        if (!OfflineNamePattern.IsMatch(name) || !Guid.TryParse(uuid, out _))
        {
            SetStatus(_localizer["profiles.wizard.nickInvalid"], isError: true);
            return;
        }

        if (!_profileRepository.UpdateProfile(SelectedProfile.Id, name, uuid))
        {
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
            return;
        }

        var editedProfile = SelectedProfile;
        IsEditing = false;
        ActiveProfileChanged?.Invoke(
            this,
            new ActiveProfileChangedEventArgs(name, editedProfile.IsOfficial));
        ClearStatus();
    }

    [RelayCommand]
    private async Task OpenProfileFolderAsync()
    {
        var selectedId = SelectedProfile?.Id;
        var profile = _profileRepository.GetProfiles()
            .FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal));
        var path = profile is null ? null : _profileManager.GetProfilePath(profile);
        if (string.IsNullOrWhiteSpace(path) || !await _uriLauncher.LaunchDirectoryAsync(path))
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
    }

    public void MoveProfile(string profileId, int targetIndex)
    {
        var profile = Profiles.FirstOrDefault(
            item => string.Equals(item.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
            return;

        var sourceIndex = Profiles.IndexOf(profile);
        targetIndex = Math.Clamp(targetIndex, 0, Profiles.Count - 1);
        if (sourceIndex == targetIndex)
            return;

        Profiles.Move(sourceIndex, targetIndex);
        _profileRepository.SetProfileOrder(Profiles.Select(item => item.Id).ToList());
    }

    [RelayCommand]
    private void DuplicateProfile(ProfileItemViewModel? profile)
    {
        if (profile is null)
            return;

        if (_profileRepository.DuplicateProfileWithoutData(profile.Id) is null)
        {
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
            return;
        }

        ClearStatus();
    }

    [RelayCommand]
    private void RequestProfileDeletion(ProfileItemViewModel? profile)
    {
        if (profile is null || profile.IsActive)
            return;

        ClearStatus();
        PendingProfileDeletion = profile;
        IsProfileDeletionOpen = true;
    }

    [RelayCommand]
    private void CancelProfileDeletion()
    {
        IsProfileDeletionOpen = false;
    }

    public void CompleteProfileDeletionClose()
    {
        PendingProfileDeletion = null;
        ClearStatus();
    }

    [RelayCommand]
    private void ConfirmProfileDeletion()
    {
        var profile = PendingProfileDeletion;
        if (profile is null)
            return;

        if (string.Equals(profile.Id, _profileRepository.GetSelectedProfileId(), StringComparison.Ordinal))
        {
            SetStatus(_localizer["profiles.deleteFailed"], isError: true);
            return;
        }

        if (!_profileRepository.DeleteProfile(profile.Id))
        {
            SetStatus(_localizer["profiles.deleteFailed"], isError: true);
            return;
        }

        IsProfileDeletionOpen = false;
    }

    private void SetCreationStep(ProfileCreationStep step)
    {
        if (_creationStep == step)
            return;

        _creationStep = step;
        OnPropertyChanged(nameof(IsCreateChoiceVisible));
        OnPropertyChanged(nameof(IsOfflineCreationVisible));
        OnPropertyChanged(nameof(IsOfficialCreationVisible));
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }

    private void CloseProfileMenus()
    {
        foreach (var profile in Profiles)
            profile.IsMenuOpen = false;
    }

    private Bitmap? LoadAvatar(string uuid)
    {
        try
        {
            var preview = _profileManager.GetAvatarPreviewForUUID(uuid);
            if (string.IsNullOrWhiteSpace(preview))
                return null;

            var separator = preview.IndexOf(',');
            if (separator < 0 || separator == preview.Length - 1)
                return null;

            var bytes = Convert.FromBase64String(preview[(separator + 1)..]);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateDefaultOfflineName()
        => $"Prism{Random.Shared.Next(1000, 10000)}";

    private string FormatPlayTime(TimeSpan playTime)
    {
        var duration = playTime < TimeSpan.Zero ? TimeSpan.Zero : playTime;
        return _localizer.Format(
            "instances.info.playtimeValue",
            (long)duration.TotalHours,
            duration.Minutes);
    }

    private string ResolveFavoriteInstance(Profile profile)
    {
        var favorite = (profile.InstancePlayTimeSeconds ?? [])
            .Where(entry => entry.Value > 0)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(favorite.Key))
            return "—";

        var instance = _instanceRepository?.FindInstanceById(favorite.Key);
        return string.IsNullOrWhiteSpace(instance?.Name) ? "—" : instance.Name;
    }

    partial void OnSelectedProfileChanged(ProfileItemViewModel? value)
    {
        foreach (var profile in Profiles)
            profile.IsSelected = ReferenceEquals(profile, value);

        EditName = value?.Name ?? string.Empty;
        EditUuid = value?.Uuid ?? string.Empty;
        IsEditing = false;
    }

    partial void OnOfflineProfileNameChanged(string value)
        => OnPropertyChanged(nameof(CanCreateOfflineProfile));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _authenticationCancellation?.Cancel();
        _profileRepository.ProfilesChanged -= OnProfilesChanged;
        _profileManager.ProfilesChanged -= OnProfilesChanged;
        foreach (var profile in Profiles)
            profile.Dispose();
        Profiles.Clear();
    }

    private enum ProfileCreationStep
    {
        None,
        ChooseType,
        Offline,
        Official
    }
}

/// <summary>
/// Provides the identity of the profile that became active
/// </summary>
public sealed class ActiveProfileChangedEventArgs(string name, bool isOfficial) : EventArgs
{
    /// <summary>
    /// Gets the display name of the activated profile
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets whether the activated profile is linked to an official Hytale account
    /// </summary>
    public bool IsOfficial { get; } = isOfficial;
}
