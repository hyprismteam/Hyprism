// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Accounts;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Hyprism.Core.Migrations;

/// <summary>
/// Applies Core-owned data migrations in dependency order before hosts consume launcher data.
/// </summary>
public sealed class CoreMigrationRunner
{
    private const string InstanceLayoutMigrationId = "2026-09-instance-layout";
    private const string FixedInstanceSelectionMigrationId = "2026-09-fixed-instance-selection";
    private const string ProfileStorageMigrationId = "2026-09-profile-storage";
    private const string ProfileSessionMigrationId = "2026-09-profile-session";
    private const string InstanceModsMigrationId = "2026-09-instance-mods";
    private const string DownloadsMigrationId = "2026-09-game-download-cache";

    private readonly AppPathConfiguration _appPath;
    private readonly JsonConfigStore _configStore;
    private readonly IInstanceMigrator _instanceMigrator;
    private readonly IInstanceRepository _instances;
    private readonly MigrationStateStore _state;
    private readonly IServiceProvider _services;
    private readonly MigrationContext _context;

    /// <summary>Creates the Core migration orchestrator</summary>
    public CoreMigrationRunner(
        AppPathConfiguration appPath,
        JsonConfigStore configStore,
        IInstanceMigrator instanceMigrator,
        IInstanceRepository instances,
        MigrationStateStore state,
        IServiceProvider services)
    {
        _appPath = appPath;
        _configStore = configStore;
        _instanceMigrator = instanceMigrator;
        _instances = instances;
        _state = state;
        _services = services;
        _context = new MigrationContext(appPath);
    }

    /// <summary>
    /// Applies all local structural migrations, then performs retryable version-name enrichment.
    /// </summary>
    /// <returns>A task that completes when the operation finishes</returns>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Config must be canonical before paths, selected IDs, and profile storage are read.
        _configStore.ApplyDeferredMigrations();

        await RunOnceAsync(
            new DelegateMigration(
                InstanceLayoutMigrationId,
                _ =>
                {
                    _instanceMigrator.MigrateLegacyData();
                    _instanceMigrator.MigrateVersionFoldersToIdFolders();
                    _instanceMigrator.MigrateBranchSubdirectoriesToFlat();
                    _instances.SyncInstancesWithConfig();
                    return Task.CompletedTask;
                }),
            cancellationToken).ConfigureAwait(false);

        await RunOnceAsync(
            new DelegateMigration(
                FixedInstanceSelectionMigrationId,
                _ =>
                {
                    if (_instanceMigrator.MigrateLegacyRollingInstancesToFixedVersions())
                        _instances.SyncInstancesWithConfig();
                    return Task.CompletedTask;
                }),
            cancellationToken).ConfigureAwait(false);

        var profiles = _services.GetRequiredService<IProfileRepository>();
        await RunOnceAsync(
            new DelegateMigration(
                ProfileStorageMigrationId,
                _ =>
                {
                    profiles.GetProfiles();
                    return Task.CompletedTask;
                }),
            cancellationToken).ConfigureAwait(false);

        await RunOnceAsync(
            new DelegateMigration(
                ProfileSessionMigrationId,
                _ =>
                {
                    new ProfileSessionMigration(_appPath, _configStore).Migrate();
                    return Task.CompletedTask;
                }),
            cancellationToken).ConfigureAwait(false);

        await RunOnceAsync(
            new DelegateMigration(
                InstanceModsMigrationId,
                _ =>
                {
                    profiles.MigrateLegacyModsLinks();
                    return Task.CompletedTask;
                }),
            cancellationToken).ConfigureAwait(false);

        await RunOnceAsync(
            new DelegateMigration(
                DownloadsMigrationId,
                _ =>
                {
                    GameDownloadCacheMigration.Migrate(_appPath.AppDir);
                    return Task.CompletedTask;
                }),
            cancellationToken).ConfigureAwait(false);

        // Version names rely on remote sources and therefore must be retried until data is available.
        var versionNames = _services.GetRequiredService<InstanceVersionNameMigrator>();
        await versionNames.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunOnceAsync(IMigration migration, CancellationToken cancellationToken)
    {
        if (_state.IsCompleted(migration.Id))
            return;

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await migration.ApplyAsync(_context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _state.MarkCompleted(migration.Id);
            Logger.Info("Migration", $"Completed {migration.Id}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not mark failed work. The next startup can retry idempotent migrations.
            Logger.Warning("Migration", $"Migration {migration.Id} failed: {exception.Message}");
        }
    }

    private sealed class DelegateMigration : IMigration
    {
        private readonly Func<CancellationToken, Task> _apply;

        public DelegateMigration(string id, Func<CancellationToken, Task> apply)
        {
            Id = id;
            _apply = apply;
        }

        public string Id { get; }

        /// <summary>Applies the delegated migration action</summary>
        /// <param name="context">Migration context supplied by the runner</param>
        /// <param name="cancellationToken">Token used to cancel the migration</param>
        /// <returns>A task that completes after the delegated action finishes</returns>
        public Task ApplyAsync(MigrationContext context, CancellationToken cancellationToken = default)
            => _apply(cancellationToken);
    }
}
