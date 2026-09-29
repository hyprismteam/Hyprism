// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Core.Migrations;

/// <summary>
/// A durable, idempotent migration of launcher-owned local data
/// </summary>
public interface IMigration
{
    /// <summary>Stable identifier persisted after a successful run</summary>
    string Id { get; }

    /// <summary>Applies the migration</summary>
    /// <param name="context">Migration context containing launcher paths and services</param>
    /// <param name="cancellationToken">Token used to cancel the migration</param>
    /// <returns>A task that completes after the migration is applied</returns>
    Task ApplyAsync(MigrationContext context, CancellationToken cancellationToken = default);
}
