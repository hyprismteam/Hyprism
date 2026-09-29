// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Migrations;

/// <summary>
/// Shared filesystem context for a launcher data migration.
/// </summary>
public sealed class MigrationContext
{
    /// <summary>Creates a migration context for one application data directory</summary>
    public MigrationContext(AppPathConfiguration appPath)
    {
        AppPath = appPath ?? throw new ArgumentNullException(nameof(appPath));
    }

    /// <summary>Application data root owned by the launcher</summary>
    public AppPathConfiguration AppPath { get; }
}
