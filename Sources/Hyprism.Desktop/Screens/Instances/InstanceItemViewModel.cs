// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstanceItemViewModel : ObservableObject
{
    public InstanceItemViewModel(
        string id,
        string name,
        string version,
        string branch,
        bool isInstalled,
        bool isManaged,
        Bitmap? icon = null)
    {
        Id = id;
        Name = name;
        Version = version;
        Branch = branch;
        IsInstalled = isInstalled;
        IsManaged = isManaged;
        Icon = icon;
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Branch { get; }
    public bool IsInstalled { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "H"
        : Name[..1].ToUpperInvariant();

    [ObservableProperty]
    private bool _isManaged;

    [ObservableProperty]
    private bool _isMenuOpen;

    [ObservableProperty]
    private bool _isRunning;
}
