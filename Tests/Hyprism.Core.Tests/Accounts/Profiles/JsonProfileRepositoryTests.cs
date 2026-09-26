// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Models;
using Hyprism.Core.Infrastructure;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Accounts;
using System.Text.Json;

namespace Hyprism.Core.Tests.Accounts.Profiles;

public class JsonProfileRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonConfigStore _config;
    private readonly Mock<ISkinRepository> _skinMock;
    private readonly Mock<IInstanceRepository> _instanceMock;
    private readonly Mock<IUserIdentityProvider> _identityMock;
    private readonly JsonProfileRepository _svc;

    public JsonProfileRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyprismPMTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new JsonConfigStore(_tempDir);
        _skinMock = new Mock<ISkinRepository>();
        _instanceMock = new Mock<IInstanceRepository>();
        _identityMock = new Mock<IUserIdentityProvider>();

        _instanceMock.Setup(i => i.GetInstanceRoot()).Returns(_tempDir);
        _instanceMock.Setup(i => i.GetInstanceRootsIncludingLegacy()).Returns([_tempDir]);

        _svc = new JsonProfileRepository(
            new AppPathConfiguration(_tempDir),
            _config,
            _skinMock.Object,
            _instanceMock.Object,
            _identityMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }


    [Fact]
    public void GetProfiles_Initially_ReturnsEmptyOrDefaultList()
    {
        var profiles = _svc.GetProfiles();
        Assert.NotNull(profiles);
    }


    [Fact]
    public void CreateProfile_ValidArgs_ReturnsProfile()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("TestUser", uuid);

        Assert.NotNull(profile);
        Assert.Equal("TestUser", profile!.Name);
    }

    [Fact]
    public void CreateProfile_AfterCreate_AppearsInGetProfiles()
    {
        var uuid = Guid.NewGuid().ToString();
        _svc.CreateProfile("Visible", uuid);

        var profiles = _svc.GetProfiles();
        Assert.Contains(profiles, p => p.Name == "Visible");
    }

    [Fact]
    public void CreateProfile_OfficialProfile_PersistsIsOfficialInProfilesJson()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("OfficialUser", uuid, isOfficial: true);

        Assert.NotNull(profile);
        Assert.True(profile!.IsOfficial);
        Assert.Contains(_svc.GetProfiles(), p => p.Id == profile.Id && p.IsOfficial);

        var profilesPath = Path.Combine(_svc.GetProfilesFolder(), "Profiles.json");
        var savedProfiles = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(profilesPath));

        Assert.NotNull(savedProfiles);
        Assert.Contains(savedProfiles!, p => p.Id == profile.Id && p.IsOfficial);
    }

    [Fact]
    public void SetProfileOrder_PersistsRequestedOrder()
    {
        var first = _svc.CreateProfile("First", Guid.NewGuid().ToString())!;
        var second = _svc.CreateProfile("Second", Guid.NewGuid().ToString())!;
        var third = _svc.CreateProfile("Third", Guid.NewGuid().ToString())!;

        _svc.SetProfileOrder([third.Id, first.Id, second.Id]);

        Assert.Equal(
            [third.Id, first.Id, second.Id],
            _svc.GetProfiles().Select(profile => profile.Id));
    }

    [Fact]
    public void CreateProfile_DuplicateName_ReturnsProfile()
    {
        // JsonProfileRepository does not enforce unique names, so both calls succeed
        var uuid = Guid.NewGuid().ToString();
        _svc.CreateProfile("Dupe", uuid);

        var second = _svc.CreateProfile("Dupe", Guid.NewGuid().ToString());
        Assert.NotNull(second);
    }


    [Fact]
    public void DeleteProfile_ExistingProfile_ReturnsTrueAndRemoves()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("ToDelete", uuid)!;

        var result = _svc.DeleteProfile(profile.Id);

        Assert.True(result);
        Assert.DoesNotContain(_svc.GetProfiles(), p => p.Id == profile.Id);
    }

    [Fact]
    public void DeleteProfile_NonExistent_ReturnsFalse()
    {
        var result = _svc.DeleteProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }


    [Fact]
    public void SwitchProfile_ById_ValidId_ReturnsTrue()
    {
        var uuid = Guid.NewGuid().ToString();
        _identityMock.Setup(i => i.GetUuidForUser(It.IsAny<string>())).Returns(uuid);

        var profile = _svc.CreateProfile("Switchable", uuid)!;
        var result = _svc.SwitchProfile(profile.Id);

        Assert.True(result);
    }

    [Fact]
    public void SwitchProfile_ById_InvalidId_ReturnsFalse()
    {
        var result = _svc.SwitchProfile(Guid.NewGuid().ToString());
        Assert.False(result);
    }

    [Fact]
    public void UpdateProfile_ValidId_ReturnsTrue()
    {
        var uuid = Guid.NewGuid().ToString();
        var profile = _svc.CreateProfile("OldName", uuid)!;

        var result = _svc.UpdateProfile(profile.Id, "NewName", null);

        Assert.True(result);
        Assert.Contains(_svc.GetProfiles(), p => p.Name == "NewName");
    }

    [Fact]
    public void UpdateProfile_NonExistentId_ReturnsFalse()
    {
        var result = _svc.UpdateProfile(Guid.NewGuid().ToString(), "Name", null);
        Assert.False(result);
    }

    [Fact]
    public void RecordPlayTime_AccumulatesProfileAndInstanceStatistics()
    {
        var profile = _svc.CreateProfile("Player", Guid.NewGuid().ToString())!;
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.RecordPlayTime(profile.Id, "instance-a", 3720));
        Assert.True(_svc.RecordPlayTime(profile.Id, "instance-a", 60));
        Assert.True(_svc.RecordPlayTime(profile.Id, "instance-b", 120));

        var saved = Assert.Single(_svc.GetProfiles());
        Assert.Equal(TimeSpan.FromSeconds(3900), saved.TotalPlaytime);
        Assert.Equal(3780, saved.InstancePlayTimeSeconds["instance-a"]);
        Assert.Equal(120, saved.InstancePlayTimeSeconds["instance-b"]);
        Assert.Equal(3, raised);
    }


    [Fact]
    public void GetProfilesFolder_ReturnsAbsolutePath()
    {
        var folder = _svc.GetProfilesFolder();
        Assert.True(Path.IsPathRooted(folder));
    }

    [Fact]
    public void GetCurrentProfileFolder_ReturnsPreparedFolderWithoutOpeningIt()
    {
        var profile = _svc.CreateProfile("FolderOwner", Guid.NewGuid().ToString())!;
        var expectedPath = Path.Combine(_svc.GetProfilesFolder(), profile.Id);
        if (Directory.Exists(expectedPath))
            Directory.Delete(expectedPath, true);

        var path = _svc.GetCurrentProfileFolder();

        Assert.Equal(expectedPath, path);
        Assert.True(Directory.Exists(expectedPath));
        Assert.Contains(
            Directory.EnumerateFiles(expectedPath),
            path => string.Equals(Path.GetFileName(path), "Profile.json", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(expectedPath, "Profile.json")));
        Assert.Equal("FolderOwner", document.RootElement.GetProperty("Username").GetString());
        Assert.True(document.RootElement.TryGetProperty("Uuid", out _));
        Assert.True(document.RootElement.TryGetProperty("CreatedAt", out _));
    }


    [Fact]
    public void GetSelectedProfileId_Initially_ReturnsStringOrEmpty()
    {
        var id = _svc.GetSelectedProfileId();
        Assert.NotNull(id);
    }

    [Fact]
    public void CreateProfile_RaisesProfilesChanged()
    {
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        var profile = _svc.CreateProfile("EventUser", Guid.NewGuid().ToString());

        Assert.NotNull(profile);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void DeleteProfile_RaisesProfilesChanged()
    {
        var profile = _svc.CreateProfile("ToDelete", Guid.NewGuid().ToString())!;
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.DeleteProfile(profile.Id));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SwitchProfile_RaisesProfilesChanged()
    {
        var first = _svc.CreateProfile("First", Guid.NewGuid().ToString())!;
        var second = _svc.CreateProfile("Second", Guid.NewGuid().ToString())!;
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.SwitchProfile(second.Id));
        Assert.Equal(second.Id, _svc.GetSelectedProfileId());
        Assert.True(_svc.SwitchProfile(first.Id));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void UpdateProfile_RaisesProfilesChanged()
    {
        var profile = _svc.CreateProfile("Editable", Guid.NewGuid().ToString())!;
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.True(_svc.UpdateProfile(profile.Id, "Renamed", null));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetProfileOrder_RaisesProfilesChanged()
    {
        var first = _svc.CreateProfile("One", Guid.NewGuid().ToString())!;
        var second = _svc.CreateProfile("Two", Guid.NewGuid().ToString())!;
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        _svc.SetProfileOrder([second.Id, first.Id]);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void FailedMutations_DoNotRaiseProfilesChanged()
    {
        var raised = 0;
        _svc.ProfilesChanged += () => raised++;

        Assert.Null(_svc.CreateProfile("", Guid.NewGuid().ToString()));
        Assert.False(_svc.DeleteProfile("missing"));
        Assert.False(_svc.SwitchProfile("missing"));
        Assert.False(_svc.UpdateProfile("missing", "Name", null));
        Assert.False(_svc.RecordPlayTime("missing", "instance", 60));
        Assert.Equal(0, raised);
    }
}
