// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using Hyprism.Core.Accounts;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Models;
using Hyprism.Desktop.Screens.Profiles;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class ProfilesViewModelTests
{
    [AvaloniaFact]
    public void CancelCreationKeepsCurrentStepUntilTheVisualTransitionCompletes()
    {
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns([]);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));

        viewModel.ShowCreateChoiceCommand.Execute(null);
        viewModel.BeginOfflineCreationCommand.Execute(null);
        viewModel.CancelCreationCommand.Execute(null);

        Assert.False(viewModel.IsCreationVisible);
        Assert.True(viewModel.IsOfflineCreationVisible);

        viewModel.CompleteCreationTransition();

        Assert.False(viewModel.IsCreateChoiceVisible);
        Assert.False(viewModel.IsOfflineCreationVisible);
        Assert.False(viewModel.IsOfficialCreationVisible);

        viewModel.ShowCreateChoiceCommand.Execute(null);

        Assert.True(viewModel.IsCreationVisible);
        Assert.True(viewModel.IsCreateChoiceVisible);
        Assert.False(viewModel.IsOfflineCreationVisible);
        Assert.False(viewModel.IsOfficialCreationVisible);
    }

    [AvaloniaFact]
    public void ReturningFromAccountTypeAlwaysRestoresOnlyTheChoiceStep()
    {
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns([]);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));

        viewModel.ShowCreateChoiceCommand.Execute(null);
        viewModel.BeginOfficialCreationCommand.Execute(null);
        viewModel.ReturnToCreationChoiceCommand.Execute(null);

        Assert.True(viewModel.IsCreationVisible);
        Assert.True(viewModel.IsCreateChoiceVisible);
        Assert.False(viewModel.IsOfflineCreationVisible);
        Assert.False(viewModel.IsOfficialCreationVisible);
    }

    [AvaloniaFact]
    public async Task OfficialSignInRequiresPointerReentryBeforeItCanBeCancelled()
    {
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var authenticator = new Mock<IHytaleAuthenticator>();
        var authenticationStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        profileRepository.Setup(repository => repository.GetProfiles()).Returns([]);
        authenticator.Setup(service => service.LoginAsync(
                It.IsAny<AuthUriPresenter>(),
                It.IsAny<CancellationToken>()))
            .Returns<AuthUriPresenter, CancellationToken>(async (_, cancellationToken) =>
            {
                authenticationStarted.TrySetResult(cancellationToken);
                var authenticationCompletion = new TaskCompletionSource<HytaleAuthSession?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(
                    () => authenticationCompletion.TrySetCanceled(cancellationToken));
                return await authenticationCompletion.Task;
            });

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"),
            authenticator.Object);
        viewModel.ShowCreateChoiceCommand.Execute(null);
        viewModel.BeginOfficialCreationCommand.Execute(null);

        var authentication = viewModel.SignInWithHytaleCommand.ExecuteAsync(null);
        var cancellationToken = await authenticationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsAuthenticating);
        Assert.False(viewModel.IsAuthenticationCancellationArmed);
        Assert.False(cancellationToken.IsCancellationRequested);

        viewModel.SignInWithHytaleCommand.Execute(null);

        Assert.False(cancellationToken.IsCancellationRequested);

        viewModel.ArmAuthenticationCancellation();
        Assert.True(viewModel.IsAuthenticationCancellationArmed);

        viewModel.SignInWithHytaleCommand.Execute(null);
        await authentication.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.False(viewModel.IsAuthenticating);
        Assert.False(viewModel.IsAuthenticationCancellationArmed);
        Assert.False(viewModel.HasStatusMessage);
    }

    [AvaloniaFact]
    public void CreateOfflineProfile_ActivatesAndSelectsNewProfile()
    {
        var profiles = new List<Profile>
        {
            new()
            {
                Id = "existing",
                Name = "Existing",
                UUID = Guid.NewGuid().ToString()
            }
        };
        var activeProfileId = "existing";
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var profileChanged = 0;

        profileRepository.Setup(repository => repository.GetProfiles()).Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns(() => activeProfileId);
        profileRepository.Setup(repository => repository.CreateProfile(
                "Offline_Player",
                It.IsAny<string>(),
                false))
            .Returns((string name, string uuid, bool _) =>
            {
                var profile = new Profile
                {
                    Id = "offline-player",
                    Name = name,
                    UUID = uuid
                };
                profiles.Add(profile);
                profileRepository.Raise(repository => repository.ProfilesChanged += null!);
                return profile;
            });
        profileRepository.Setup(repository => repository.SwitchProfile(It.IsAny<string>()))
            .Callback<string>(id =>
            {
                activeProfileId = id;
                profileRepository.Raise(repository => repository.ProfilesChanged += null!);
            })
            .Returns(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        viewModel.ActiveProfileChanged += (_, _) => profileChanged++;

        viewModel.ShowCreateChoiceCommand.Execute(null);
        viewModel.BeginOfflineCreationCommand.Execute(null);
        viewModel.OfflineProfileName = "Offline_Player";
        viewModel.CreateOfflineProfileCommand.Execute(null);

        profileRepository.Verify(repository => repository.CreateProfile(
            "Offline_Player",
            It.Is<string>(uuid => IsGuid(uuid)),
            false), Times.Once);
        profileRepository.Verify(repository => repository.SwitchProfile("offline-player"), Times.Once);
        Assert.Equal("offline-player", viewModel.SelectedProfile?.Id);
        Assert.True(viewModel.SelectedProfile?.IsActive);
        Assert.Equal(1, profileChanged);
        Assert.False(viewModel.IsCreationVisible);
        Assert.True(viewModel.IsOfflineCreationVisible);
        Assert.False(viewModel.HasStatusMessage);

        viewModel.CompleteCreationTransition();

        Assert.False(viewModel.IsCreateChoiceVisible);
        Assert.False(viewModel.IsOfflineCreationVisible);
        Assert.False(viewModel.IsOfficialCreationVisible);
    }

    [AvaloniaFact]
    public async Task OfficialSignInDoesNotShowSuccessStatusAfterCreatingProfile()
    {
        const string profileUuid = "11111111-1111-1111-1111-111111111111";
        var profiles = new List<Profile>();
        var activeProfileId = string.Empty;
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var authenticator = new Mock<IHytaleAuthenticator>();
        profileRepository.Setup(repository => repository.GetProfiles())
            .Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId())
            .Returns(() => activeProfileId);
        profileRepository.Setup(repository => repository.CreateProfile(
                "Official_Player",
                profileUuid,
                true))
            .Returns((string name, string uuid, bool _) =>
            {
                var profile = new Profile
                {
                    Id = "official-player",
                    Name = name,
                    UUID = uuid,
                    IsOfficial = true
                };
                profiles.Add(profile);
                return profile;
            });
        profileRepository.Setup(repository => repository.SwitchProfile("official-player"))
            .Callback(() => activeProfileId = "official-player")
            .Returns(true);
        authenticator.Setup(service => service.LoginAsync(
                It.IsAny<AuthUriPresenter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HytaleAuthSession
            {
                Username = "Official_Player",
                UUID = profileUuid,
                AccountProfiles = [("Official_Player", profileUuid)]
            });

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"),
            authenticator.Object);

        viewModel.ShowCreateChoiceCommand.Execute(null);
        viewModel.BeginOfficialCreationCommand.Execute(null);
        await viewModel.SignInWithHytaleCommand.ExecuteAsync(null);

        Assert.Equal("official-player", viewModel.SelectedProfile?.Id);
        Assert.False(viewModel.HasStatusMessage);
    }

    [Fact]
    public void MoveProfile_ReordersCardsAndPersistsOrder()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "first", Name = "First", UUID = Guid.NewGuid().ToString() },
            new() { Id = "second", Name = "Second", UUID = Guid.NewGuid().ToString() },
            new() { Id = "third", Name = "Third", UUID = Guid.NewGuid().ToString() }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(profiles);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("first");

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));

        viewModel.MoveProfile("third", 0);

        Assert.Equal(["third", "first", "second"], viewModel.Profiles.Select(profile => profile.Id));
        Assert.Equal("first", viewModel.SelectedProfile?.Id);
        profileRepository.Verify(
            repository => repository.SetProfileOrder(
                It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "third", "first", "second" }))),
            Times.Once);
    }

    [AvaloniaFact]
    public void SelectProfile_OnlyOpensDetails_UntilActivationIsRequested()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() },
            new() { Id = "preview", Name = "Preview", UUID = Guid.NewGuid().ToString() }
        };
        var activeProfileId = "active";
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var profileChanged = 0;
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns(() => activeProfileId);
        profileRepository.Setup(repository => repository.SwitchProfile("preview"))
            .Callback(() =>
            {
                activeProfileId = "preview";
                profileRepository.Raise(repository => repository.ProfilesChanged += null!);
            })
            .Returns(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        viewModel.ActiveProfileChanged += (_, _) => profileChanged++;
        var preview = viewModel.Profiles.Single(profile => profile.Id == "preview");

        viewModel.SelectProfileCommand.Execute(preview);

        Assert.Equal("preview", viewModel.SelectedProfile?.Id);
        Assert.True(viewModel.SelectedProfile?.IsSelected);
        Assert.False(viewModel.SelectedProfile?.IsActive);
        Assert.True(viewModel.CanActivateSelectedProfile);
        profileRepository.Verify(repository => repository.SwitchProfile(It.IsAny<string>()), Times.Never);

        viewModel.ActivateSelectedProfileCommand.Execute(null);

        profileRepository.Verify(repository => repository.SwitchProfile("preview"), Times.Once);
        Assert.Equal("preview", viewModel.SelectedProfile?.Id);
        Assert.True(viewModel.SelectedProfile?.IsActive);
        Assert.False(viewModel.CanActivateSelectedProfile);
        Assert.Equal(1, profileChanged);
    }

    [Fact]
    public void RequestProfileDeletion_RejectsSelectedLauncherProfile()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() },
            new() { Id = "inactive", Name = "Inactive", UUID = Guid.NewGuid().ToString() }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(profiles);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active");

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));

        viewModel.RequestProfileDeletionCommand.Execute(
            viewModel.Profiles.Single(profile => profile.Id == "active"));

        Assert.Null(viewModel.PendingProfileDeletion);

        var inactive = viewModel.Profiles.Single(profile => profile.Id == "inactive");
        viewModel.RequestProfileDeletionCommand.Execute(inactive);

        Assert.Same(inactive, viewModel.PendingProfileDeletion);
    }

    [Fact]
    public void ConfirmProfileDeletion_KeepsConfirmationOpenUntilDeletionSucceeds()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() },
            new() { Id = "inactive", Name = "Inactive", UUID = Guid.NewGuid().ToString() }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(profiles);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active");
        profileRepository.SetupSequence(repository => repository.DeleteProfile("inactive"))
            .Returns(false)
            .Returns(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));

        viewModel.RequestProfileDeletionCommand.Execute(
            viewModel.Profiles.Single(profile => profile.Id == "inactive"));
        profileRepository.Verify(repository => repository.DeleteProfile(It.IsAny<string>()), Times.Never);

        viewModel.ConfirmProfileDeletionCommand.Execute(null);
        Assert.True(viewModel.HasPendingProfileDeletion);
        Assert.True(viewModel.IsStatusError);
        Assert.Equal("Could not delete the profile.", viewModel.StatusMessage);

        viewModel.ConfirmProfileDeletionCommand.Execute(null);
        Assert.False(viewModel.IsProfileDeletionOpen);
        Assert.True(viewModel.HasPendingProfileDeletion);
        viewModel.CompleteProfileDeletionClose();
        Assert.False(viewModel.HasPendingProfileDeletion);
        Assert.False(viewModel.IsStatusError);
        profileRepository.Verify(repository => repository.DeleteProfile("inactive"), Times.Exactly(2));
    }

    [Fact]
    public async Task OpenProfileFolder_UsesDisplayedProfileInsteadOfActiveProfile()
    {
        var profiles = new List<Profile>
        {
            new() { Id = "active", Name = "Active", UUID = Guid.NewGuid().ToString() },
            new() { Id = "preview", Name = "Preview", UUID = Guid.NewGuid().ToString() }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns(() => profiles.ToList());
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns("active");
        profileManager.Setup(manager => manager.GetProfilePath(
                It.Is<Profile>(profile => profile.Id == "preview")))
            .Returns("/tmp/hyprism-preview-profile");
        uriLauncher.Setup(launcher => launcher.LaunchDirectoryAsync("/tmp/hyprism-preview-profile"))
            .ReturnsAsync(true);

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"));
        viewModel.SelectProfileCommand.Execute(viewModel.Profiles.Single(profile => profile.Id == "preview"));

        await viewModel.OpenProfileFolderCommand.ExecuteAsync(null);

        profileManager.Verify(manager => manager.GetProfilePath(
            It.Is<Profile>(profile => profile.Id == "preview")), Times.Once);
        uriLauncher.Verify(
            launcher => launcher.LaunchDirectoryAsync("/tmp/hyprism-preview-profile"),
            Times.Once);
    }

    [Fact]
    public void ProfileStatistics_ShowTotalPlayTimeAndMostPlayedInstance()
    {
        var profile = new Profile
        {
            Id = "profile",
            Name = "Player",
            UUID = Guid.NewGuid().ToString(),
            TotalPlaytime = TimeSpan.FromSeconds(11040),
            InstancePlayTimeSeconds = new Dictionary<string, long>
            {
                ["secondary"] = 3600,
                ["favorite"] = 7200
            }
        };
        var profileManager = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var instanceRepository = new Mock<IInstanceRepository>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns([profile]);
        profileRepository.Setup(repository => repository.GetSelectedProfileId()).Returns(profile.Id);
        instanceRepository.Setup(repository => repository.FindInstanceById("favorite"))
            .Returns(new InstanceInfo { Id = "favorite", Name = "Favorite Build" });

        using var viewModel = new ProfilesViewModel(
            profileManager.Object,
            profileRepository.Object,
            uriLauncher.Object,
            new StringLocalizer("en-US"),
            instanceRepository: instanceRepository.Object);

        Assert.Equal("3 h 4 min", viewModel.SelectedProfile?.PlayTime);
        Assert.Equal("Favorite Build", viewModel.SelectedProfile?.FavoriteInstance);
    }

    private static bool IsGuid(string value)
        => Guid.TryParse(value, out _);
}
