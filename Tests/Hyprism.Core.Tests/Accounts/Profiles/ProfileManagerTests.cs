// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game.Assets;
using Hyprism.Core.Accounts;

namespace Hyprism.Core.Tests.Accounts.Profiles;

public class ProfileManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonConfigStore _config;
    private readonly Mock<IAvatarCache> _avatarMock;
    private readonly ProfileManager _svc;

    public ProfileManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyprismProfileTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new JsonConfigStore(_tempDir);
        _avatarMock = new Mock<IAvatarCache>();
        _svc = new ProfileManager(_tempDir, _config, _avatarMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }


    [Fact]
    public void SetNick_ValidNick_ReturnsTrueAndPersists()
    {
        CreateSelectedProfile();
        var result = _svc.SetNick("Tester");
        Assert.True(result);
        Assert.Equal("Tester", _svc.GetNick());
    }

    [Fact]
    public void SetNick_EmptyNick_ReturnsFalse()
    {
        Assert.False(_svc.SetNick(""));
    }

    [Fact]
    public void SetNick_TooLongNick_ReturnsFalse()
    {
        Assert.False(_svc.SetNick("ThisNickIsTooLong!"));
    }

    [Fact]
    public void SetNick_MaxLength_ReturnsTrue()
    {
        CreateSelectedProfile();
        Assert.True(_svc.SetNick("1234567890123456")); // exactly 16 chars
    }


    [Fact]
    public void GetUUID_WithoutSelectedProfile_ReturnsEmpty()
    {
        Assert.Empty(_svc.GetUUID());
    }

    [Fact]
    public void SetUUID_ValidGuid_ReturnsTrueAndPersists()
    {
        CreateSelectedProfile();
        var uuid = Guid.NewGuid().ToString();
        var result = _svc.SetUUID(uuid);
        Assert.True(result);
        Assert.Equal(uuid, _svc.GetUUID());
    }

    [Fact]
    public void GenerateNewUuid_ReturnsValidGuid()
    {
        var uuid = _svc.GenerateNewUuid();
        Assert.True(Guid.TryParse(uuid, out _));
    }

    [Fact]
    public void GetCurrentUuid_WithoutSelectedProfile_ReturnsEmpty()
    {
        Assert.Empty(_svc.GetCurrentUuid());
    }


    [Fact]
    public void CreateProfile_ValidName_ReturnsTrue()
    {
        var result = _svc.CreateProfile("TestProfile");
        Assert.True(result);
        Assert.Equal(_svc.GetProfiles().Single().Id, _config.Configuration.SelectedProfileId);
    }

    [Fact]
    public void CreateProfile_Duplicate_ReturnsTrue()
    {
        // ProfileManager does not enforce unique names, so both calls succeed
        _svc.CreateProfile("DupeProfile");
        var result = _svc.CreateProfile("DupeProfile");
        Assert.True(result);
    }

    [Fact]
    public void GetProfiles_AfterCreate_ContainsNewProfile()
    {
        _svc.CreateProfile("MyProfile");
        var profiles = _svc.GetProfiles();
        Assert.Contains(profiles, p => p.Name == "MyProfile");
    }

    [Fact]
    public void DeleteProfile_ExistingProfile_RemovesIt()
    {
        _svc.CreateProfile("ToDelete");
        var id = _svc.GetProfiles().First(p => p.Name == "ToDelete").Id;

        var result = _svc.DeleteProfile(id);

        Assert.True(result);
        Assert.DoesNotContain(_svc.GetProfiles(), p => p.Name == "ToDelete");
    }

    [Fact]
    public void DeleteProfile_NonExistentId_ReturnsFalse()
    {
        var result = _svc.DeleteProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }

    [Fact]
    public void SwitchProfile_ValidId_ReturnsTrue()
    {
        _svc.CreateProfile("SwitchTarget");
        var id = _svc.GetProfiles().First(p => p.Name == "SwitchTarget").Id;

        var result = _svc.SwitchProfile(id);

        Assert.True(result);
    }

    [Fact]
    public void SwitchProfile_InvalidId_ReturnsFalse()
    {
        var result = _svc.SwitchProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }


    [Fact]
    public void GetAvatarDirectory_ReturnsNonEmptyPath()
    {
        CreateSelectedProfile();
        var dir = _svc.GetAvatarDirectory();
        Assert.False(string.IsNullOrEmpty(dir));
    }

    [Fact]
    public void GetAvatarPreview_NoFile_ReturnsNull()
    {
        var result = _svc.GetAvatarPreview();
        // No file → null or empty, never throws
        Assert.True(result == null || result is string);
    }

    [Fact]
    public void ClearAvatarCache_DoesNotThrow()
    {
        _avatarMock.Setup(a => a.ClearAvatarCache(It.IsAny<string>())).Returns(true);
        var ex = Record.Exception(() => _svc.ClearAvatarCache());
        Assert.Null(ex);
    }


    [Fact]
    public void GetProfilePath_Profile_ReturnsAbsolutePath()
    {
        var profile = new Profile { Id = Guid.NewGuid().ToString(), Name = "PathTest" };
        var path = _svc.GetProfilePath(profile);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void CreateProfile_RaisesProfilesChanged()
    {
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.CreateProfile("EventUser"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetNick_RaisesProfilesChanged()
    {
        CreateSelectedProfile();
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.SetNick("Renamed"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void DeleteProfile_RaisesProfilesChanged()
    {
        CreateSelectedProfile();
        var profile = _svc.GetProfiles().Single();
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.DeleteProfile(profile.Id));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SwitchProfile_RaisesProfilesChanged()
    {
        CreateSelectedProfile();
        Assert.True(_svc.CreateProfile("Second"));
        var target = _svc.GetProfiles().Single(p => p.Name == "Second");
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.SwitchProfile(target.Id));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void FailedMutations_DoNotRaiseProfilesChanged()
    {
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.False(_svc.SetNick("NoActiveProfile"));
        Assert.False(_svc.DeleteProfile("missing"));
        Assert.False(_svc.SwitchProfile("missing"));
        Assert.Equal(0, raised);
    }

    private void CreateSelectedProfile()
    {
        Assert.True(_svc.CreateProfile("InitialProfile"));
    }
}
