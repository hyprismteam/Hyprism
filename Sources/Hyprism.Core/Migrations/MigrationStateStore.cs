// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Migrations;

/// <summary>
/// Persists completed durable migration identifiers independently of launcher release version.
/// </summary>
public sealed class MigrationStateStore
{
    private const string FileName = "MigrationState.json";
    private readonly string _path;
    private readonly Lock _lock = new();

    /// <summary>Creates a state store below the application data root</summary>
    public MigrationStateStore(AppPathConfiguration appPath)
    {
        _path = Path.Combine(appPath.AppDir, FileName);
    }

    /// <summary>Returns whether a migration completed successfully</summary>
    /// <returns>true when the migration is marked as completed; otherwise false</returns>
    public bool IsCompleted(string migrationId)
    {
        lock (_lock)
            return Read().Completed.ContainsKey(migrationId);
    }

    /// <summary>Records completion only after the migration has fully returned</summary>
    public void MarkCompleted(string migrationId)
    {
        lock (_lock)
        {
            var state = Read();
            state.Completed[migrationId] = DateTime.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonDefaults.Indented));
        }
    }

    private State Read()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(_path), JsonDefaults.CaseInsensitive) ?? new State();
        }
        catch (Exception exception)
        {
            Logger.Warning("Migration", $"Could not read migration state: {exception.Message}");
        }

        return new State();
    }

    private sealed class State
    {
        public Dictionary<string, DateTime> Completed { get; set; } = [];
    }
}
