// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Integrations.Hytale;

namespace Hyprism.Core.Game.Sources;

internal static class MirrorHeaderResolver
{
    /// <summary>Expands launcher placeholders in a mirror's request headers.</summary>
    /// <param name="headers">Configured header templates.</param>
    /// <param name="httpClient">Client used to fetch the current launcher version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolved headers, or null when none are configured.</returns>
    public static async Task<Dictionary<string, string>?> ResolveAsync(
        Dictionary<string, string>? headers,
        HttpClient httpClient,
        CancellationToken ct)
    {
        if (headers is null || headers.Count == 0)
            return null;

        string? launcherVersion = null;
        var resolved = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers)
        {
            var expanded = value;
            if (value.Contains("{hytaleVersion}", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("{hytaleAgent}", StringComparison.OrdinalIgnoreCase))
            {
                launcherVersion ??= await HytaleLauncherHeaders.GetLauncherVersionAsync(httpClient, ct);
                expanded = expanded.Replace("{hytaleVersion}", launcherVersion, StringComparison.OrdinalIgnoreCase)
                    .Replace("{hytaleAgent}", $"hytale-launcher/{launcherVersion}", StringComparison.OrdinalIgnoreCase);
            }

            resolved[name] = expanded;
        }

        return resolved;
    }
}
