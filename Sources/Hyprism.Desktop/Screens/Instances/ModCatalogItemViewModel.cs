// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Models;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class ModCatalogItemViewModel(
    string id,
    string name,
    string author,
    string summary,
    string latestFileId,
    string slug = "",
    string iconUrl = "",
    int downloadCount = 0,
    IReadOnlyList<string>? screenshotUrls = null,
    string installedFileId = "",
    string recommendedFileId = "",
    ModCompatibilityStatus compatibility = ModCompatibilityStatus.Unknown,
    string compatibilityLabel = "",
    string authorAvatarUrl = "",
    string recommendedVersionLabel = "",
    IReadOnlyList<ModDependency>? dependencies = null,
    string source = "curseforge",
    string pageUrl = "") : ObservableObject, IDisposable
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Author { get; } = author;
    public string AuthorAvatarUrl { get; } = authorAvatarUrl;
    public string Summary { get; } = summary;
    public string LatestFileId { get; } = latestFileId;
    public string Slug { get; } = slug;
    public string Source { get; } = source;
    public string SourceDisplayName => Source == "modifold" ? "Modifold" : "CurseForge";
    public string PageUrl { get; } = string.IsNullOrWhiteSpace(pageUrl)
        ? string.IsNullOrWhiteSpace(slug)
            ? $"https://www.curseforge.com/hytale/mods/{id}"
            : $"https://www.curseforge.com/hytale/mods/{slug}"
        : pageUrl;
    public string IconUrl { get; } = iconUrl;
    public int DownloadCount { get; } = downloadCount;
    public IReadOnlyList<string> ScreenshotUrls { get; } = screenshotUrls ?? [];
    public string RecommendedFileId { get; } = recommendedFileId;
    public string RecommendedVersionLabel { get; } = string.IsNullOrWhiteSpace(recommendedVersionLabel)
        ? latestFileId
        : recommendedVersionLabel;
    public ModCompatibilityStatus Compatibility { get; } = compatibility;
    public string CompatibilityLabel { get; } = compatibilityLabel;
    public IReadOnlyList<ModDependency> Dependencies { get; private set; } = dependencies ?? [];
    public bool IsCompatible => Compatibility is ModCompatibilityStatus.Compatible;
    public bool IsIncompatible => Compatibility is ModCompatibilityStatus.Incompatible;
    public bool IsCompatibilityUnknown => Compatibility is ModCompatibilityStatus.Unknown;

    [ObservableProperty]
    private string _installedFileId = installedFileId;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "M"
        : Name[..1].ToUpperInvariant();

    public string AuthorInitial => string.IsNullOrWhiteSpace(Author)
        ? "?"
        : Author[..1].ToUpperInvariant();

    public string CurseForgeUrl => PageUrl;

    public string DownloadCountLabel => FormatDownloads(DownloadCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isInstalling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAuthorAvatar))]
    private Bitmap? _authorAvatar;

    public bool CanInstall => !IsInstalling && !IsInstalled;
    public bool CanSelect => CanInstall && !IsIncompatible && !string.IsNullOrWhiteSpace(RecommendedFileId);

    public bool ShowsIcon => Icon is not null;
    public bool ShowsAuthorAvatar => AuthorAvatar is not null;

    public void SetDependencies(IReadOnlyList<ModDependency> dependencies)
    {
        Dependencies = dependencies ?? [];
        OnPropertyChanged(nameof(Dependencies));
    }

    partial void OnIconChanged(Bitmap? value)
        => OnPropertyChanged(nameof(ShowsIcon));

    partial void OnIconChanging(Bitmap? value)
        => Icon?.Dispose();

    partial void OnAuthorAvatarChanging(Bitmap? value)
        => AuthorAvatar?.Dispose();

    public void Dispose()
    {
        Icon = null;
        AuthorAvatar = null;
    }

    private static string FormatDownloads(int downloads)
        => downloads switch
        {
            >= 1_000_000 => $"{downloads / 1_000_000.0:0.#}M",
            >= 1_000 => $"{downloads / 1_000.0:0.#}k",
            _ => downloads.ToString(System.Globalization.CultureInfo.CurrentCulture)
        };
}
