// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Hyprism.Core.Migrations;
using Hyprism.Core.Models;

namespace Hyprism.Core.Infrastructure;

/// <summary>
/// Manages launcher configuration persistence including loading, saving, and automatic migrations.
/// Configuration is stored as JSON in the application data directory.
/// </summary>
public class JsonConfigStore : IConfigStore
{
    private readonly string _configPath;
    private readonly bool _deferLegacyMigrations;
    private Config _config;

    /// <inheritdoc/>
    public Config Configuration => _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConfigStore"/> class.
    /// Loads existing configuration or creates a new one with default values.
    /// </summary>
    /// <param name="appDataPath">The application data directory path where Config.json is stored.</param>
    /// <param name="deferLegacyMigrations">Whether legacy migrations should be deferred to the startup migration runner</param>
    public JsonConfigStore(string appDataPath, bool deferLegacyMigrations = false)
    {
        Directory.CreateDirectory(appDataPath);
        _configPath = LauncherJsonFile.GetPath(appDataPath, "Config.json", "config.json");
        _deferLegacyMigrations = deferLegacyMigrations;
        _config = LoadConfig(applyLegacyMigrations: !deferLegacyMigrations);
    }

    /// <summary>
    /// Loads configuration from disk and applies any necessary migrations.
    /// Creates a new configuration with defaults if file doesn't exist or is invalid.
    /// </summary>
    /// <returns>The loaded or newly created configuration.</returns>
    private Config LoadConfig(bool applyLegacyMigrations)
    {
        Config config;

        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var profileConfigMigrated = false;
                if (applyLegacyMigrations)
                {
                    json = LegacyProfileConfigMigration.Migrate(
                        Path.GetDirectoryName(_configPath)!,
                        json,
                        out profileConfigMigrated);
                }
                config = JsonSerializer.Deserialize<Config>(json, JsonDefaults.CaseInsensitive) ?? new Config();
                var retiredKeys = config.UnknownProperties?.Keys
                    .Where(key => key.Equals("MusicEnabled", StringComparison.OrdinalIgnoreCase) ||
                                  key.Equals("ShowDiscordAnnouncements", StringComparison.OrdinalIgnoreCase) ||
                                  key.Equals("DismissedAnnouncementIds", StringComparison.OrdinalIgnoreCase))
                    .ToArray() ?? [];
                foreach (var key in retiredKeys)
                    config.UnknownProperties!.Remove(key);

                Logger.Info("Config", $"Loaded config - Language: '{config.Language}'");

                bool needsSave = applyLegacyMigrations &&
                                 (profileConfigMigrated || retiredKeys.Length > 0 || !UsesPascalCaseRootProperties(json));

#pragma warning disable CS0618 // Using obsolete fields for migration
                if (applyLegacyMigrations && config.VersionType == "latest")
                {
                    config.VersionType = "release";
                    needsSave = true;
                }
#pragma warning restore CS0618

                if (needsSave)
                {
                    _config = config;
                    SaveConfig();
                }

                return config;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to load config: {ex.Message}");
        }

        // New config starts without a selected profile. Onboarding owns profile creation
        config = new Config();

        _config = config;
        SaveConfig();
        return config;
    }

    /// <summary>
    /// Applies deferred legacy storage migrations and reloads the canonical configuration.
    /// The Core migration runner calls this before any repository reads configuration-backed data.
    /// </summary>
    public void ApplyDeferredMigrations()
    {
        if (!_deferLegacyMigrations)
            return;

        _config = LoadConfig(applyLegacyMigrations: true);
    }

    /// <inheritdoc/>
    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, JsonDefaults.IndentedUnsafeRelaxed);

            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to save config: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void ResetConfig()
    {
        _config = new Config();
        SaveConfig();
    }

    private static bool UsesPascalCaseRootProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.EnumerateObject().All(property =>
                property.Name.Length == 0
                || !char.IsLetter(property.Name[0])
                || char.IsUpper(property.Name[0]));
    }

    /// <inheritdoc/>
    public Task<string?> SetInstanceDirectoryAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _config.InstanceDirectory = null!;
                SaveConfig();
                Logger.Success("Config", "Instance directory cleared, using default");
                return Task.FromResult<string?>(null);
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_configPath)!, expanded));
            }

            Directory.CreateDirectory(expanded);

            _config.InstanceDirectory = expanded;
            SaveConfig();

            Logger.Success("Config", $"Instance directory set to {expanded}");
            return Task.FromResult<string?>(expanded);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to set instance directory: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }
}
