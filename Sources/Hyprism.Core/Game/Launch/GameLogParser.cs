// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace Hyprism.Core.Game.Launch;

/// <summary>A structured record from a game process output stream</summary>
public sealed record GameLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Source,
    string Message,
    bool IsTrace = false);

/// <summary>Parses HytaleClient records and associates continuation lines with their record</summary>
public sealed class GameLogParser
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss"
    ];

    private GameLogEntry? _current;

    /// <summary>Parses one output line and retains its header for later continuation lines</summary>
    /// <returns>The parsed log entry</returns>
    public GameLogEntry Parse(string line, string fallbackLevel = "OUT")
    {
        var fields = line.Split('|', 4);
        if (fields.Length == 4 &&
            DateTime.TryParseExact(fields[0], TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var timestamp))
        {
            var level = NormalizeLevel(fields[1]);
            var entry = new GameLogEntry(
                new DateTimeOffset(timestamp), level, fields[2].Trim(), fields[3],
                level == "TRACE");
            _current = entry;
            return entry;
        }

        var trace = IsTraceLine(line);
        var continuation = trace || line.StartsWith(' ') || line.StartsWith('\t') ||
                           line.Contains("Exception:", StringComparison.Ordinal);
        var parent = continuation ? _current : null;
        return new GameLogEntry(
            parent?.Timestamp ?? DateTimeOffset.Now,
            trace ? "TRACE" : parent?.Level ?? NormalizeLevel(fallbackLevel),
            parent?.Source ?? "Game",
            line.Trim(),
            trace);
    }

    /// <summary>Maps game and launcher severity names to one set of labels</summary>
    /// <returns>The normalized severity label</returns>
    public static string NormalizeLevel(string level)
        => level.Trim().ToUpperInvariant() switch
        {
            "ERR" or "ERROR" or "SEVERE" => "ERROR",
            "WRN" or "WARN" or "WARNING" => "WARN",
            "DBG" or "DEBUG" => "DEBUG",
            "TRC" or "TRACE" => "TRACE",
            _ => "INFO"
        };

    /// <summary>Returns whether a line belongs to an exception stack trace</summary>
    /// <returns>True for a stack trace line</returns>
    public static bool IsTraceLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("at ", StringComparison.Ordinal) ||
               trimmed.StartsWith("--- End of stack trace", StringComparison.Ordinal) ||
               trimmed.StartsWith("Caused by:", StringComparison.Ordinal);
    }
}
