// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceModItemViewModel(
    string id,
    string name,
    string version,
    string author,
    bool isEnabled,
    string iconUrl = "",
    string curseForgeId = "",
    int releaseType = 1,
    string source = "",
    string pageUrl = "",
    string releaseLabel = "") : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Version { get; } = version;
    public string Author { get; } = author;
    public string IconUrl { get; } = iconUrl;
    public string CurseForgeId { get; } = curseForgeId;
    public int ReleaseType { get; } = releaseType;
    public string Source { get; } = source;
    public string PageUrl { get; } = pageUrl;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "M"
        : Name[..1].ToUpperInvariant();

    public bool HasExternalPage =>
        !string.IsNullOrWhiteSpace(CurseForgeId) || !string.IsNullOrWhiteSpace(Name);

    public string CurseForgeUrl => !string.IsNullOrWhiteSpace(PageUrl)
        ? PageUrl
        : !string.IsNullOrWhiteSpace(CurseForgeId)
            ? $"https://www.curseforge.com/hytale/mods/{CurseForgeId}"
            : $"https://www.curseforge.com/hytale/mods/search?search={Uri.EscapeDataString(Name)}";

    public bool IsRelease => ReleaseType is not (2 or 3);
    public bool IsBeta => ReleaseType == 2;
    public bool IsAlpha => ReleaseType == 3;

    public string ReleaseBadge => !string.IsNullOrWhiteSpace(releaseLabel) ? releaseLabel : ReleaseType switch
    {
        2 => "Beta",
        3 => "Alpha",
        _ => "Release"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isEnabled = isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsUpdateBadge))]
    [NotifyPropertyChangedFor(nameof(UpdateBadgeText))]
    private string _updateVersion = string.Empty;

    [ObservableProperty]
    private Bitmap? _icon;

    public bool CanInteract => !IsBusy;

    public bool ShowsUpdateBadge => !string.IsNullOrWhiteSpace(UpdateVersion);

    public string UpdateBadgeText => UpdateVersion;

    public bool ShowsIcon => Icon is not null;

    partial void OnIconChanged(Bitmap? value)
        => OnPropertyChanged(nameof(ShowsIcon));
}
