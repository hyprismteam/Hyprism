// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System;

namespace Hyprism.Core.Models;

/// <summary>
/// A user profile with DDID and display name.
/// </summary>
public class Profile
{
    /// <summary>Unique profile identifier (UUID v4).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Player UUID used for the game session.</summary>
    public string UUID { get; set; } = "";
    /// <summary>Display name shown in the launcher and in-game (offline mode).</summary>
    public string Name { get; set; } = "";
    /// <summary>Whether this profile is linked to an official Hytale account.</summary>
    public bool IsOfficial { get; set; } = false;
    /// <summary>Total accumulated play time across all instances.</summary>
    public TimeSpan TotalPlaytime { get; set; } = TimeSpan.Zero;
    /// <summary>UTC timestamp when the profile was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
