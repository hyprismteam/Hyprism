// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hyprism.Desktop.Screens.Profiles;

/// <summary>
/// Presentation data for one saved launcher profile.
/// </summary>
public sealed partial class ProfileItemViewModel : ObservableObject, IDisposable
{
    public ProfileItemViewModel(
        string id,
        string name,
        string uuid,
        bool isOfficial,
        bool isActive,
        bool isSelected,
        string accountType,
        string playTime,
        Bitmap? avatar)
    {
        Id = id;
        Name = name;
        Uuid = uuid;
        IsOfficial = isOfficial;
        IsActive = isActive;
        IsSelected = isSelected;
        AccountType = accountType;
        PlayTime = playTime;
        Avatar = avatar;
    }

    public string Id { get; }
    public string Name { get; }
    public string Uuid { get; }
    public bool IsOfficial { get; }
    public string AccountType { get; }
    public string PlayTime { get; }
    public Bitmap? Avatar { get; }
    public bool HasAvatar => Avatar is not null;
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isMenuOpen;

    public void Dispose()
        => Avatar?.Dispose();
}
