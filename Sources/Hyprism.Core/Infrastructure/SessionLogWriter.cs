// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Hyprism.Core.Game.Launch;

namespace Hyprism.Core.Infrastructure;

/// <summary>
/// Appends timestamped records to one file in the current log session.
/// </summary>
public sealed class SessionLogWriter
{
    private static readonly ConcurrentDictionary<string, object> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fileLock;

    /// <summary>Creates a writer for a session log file</summary>
    /// <param name="filePath">Path to the log file</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is not a valid path</exception>
    public SessionLogWriter(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _fileLock = FileLocks.GetOrAdd(FilePath, static _ => new object());
    }

    /// <summary>Absolute path to the file receiving log records</summary>
    public string FilePath { get; }

    /// <summary>Appends a timestamped log record to the file</summary>
    /// <param name="level">Log severity label</param>
    /// <param name="source">Component that produced the record</param>
    /// <param name="message">Message text</param>
    public void Write(string level, string source, string message)
        => Write(DateTimeOffset.Now, level, source, message);

    /// <summary>Appends a game record using its original timestamp and source</summary>
    public void Write(DateTimeOffset timestamp, string level, string source, string message)
    {
        var output = new StringBuilder();
        foreach (var part in message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trace = GameLogParser.IsTraceLine(part);
            output.Append(timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture))
                .Append('|').Append(trace ? "TRACE" : GameLogParser.NormalizeLevel(level))
                .Append('|').Append(source)
                .Append('|').Append(part.TrimEnd('\r'))
                .AppendLine();
        }

        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(FilePath, output.ToString());
            }
            catch
            {
            }
        }
    }
}
