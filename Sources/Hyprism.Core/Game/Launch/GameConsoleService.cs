// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Infrastructure;
using System.Collections.Concurrent;

namespace Hyprism.Core.Game.Launch;

/// <summary>
/// A single console line produced by a game process or by the launcher on its behalf
/// </summary>
/// <param name="InstanceId">Stable instance identifier the line belongs to.</param>
/// <param name="Level">Normalized severity: DEBUG, INFO, WARN, ERROR, or TRACE.</param>
/// <param name="Text">Message without a trailing newline.</param>
/// <param name="Timestamp">Local time of the game record or capture.</param>
/// <param name="Source">Component that emitted the record.</param>
/// <param name="IsTrace">Whether this is a stack trace line.</param>
public sealed record GameConsoleLine(
    string InstanceId, string Level, string Text, DateTimeOffset Timestamp,
    string Source, bool IsTrace);

/// <summary>
/// Provides details when a game console line is captured
/// </summary>
public sealed class GameConsoleLineEventArgs(GameConsoleLine line) : EventArgs
{
    /// <summary>
    /// Gets the captured line
    /// </summary>
    public GameConsoleLine Line { get; } = line;
}

/// <summary>
/// Buffers live game process output per instance and notifies subscribers as lines arrive
/// </summary>
public interface IGameConsoleService
{
    /// <summary>
    /// Raised for every captured line, including lines appended before a subscriber attached
    /// only when reading <see cref="GetLines"/>
    /// </summary>
    event EventHandler<GameConsoleLineEventArgs>? LineReceived;

    /// <summary>
    /// Captures one console line for an instance
    /// </summary>
    /// <param name="instanceId">Stable instance identifier</param>
    /// <param name="level">Severity tag such as INFO, WARN, ERROR, or TRACE</param>
    /// <param name="text">Message text</param>
    /// <param name="timestamp">Timestamp supplied by the game, if available</param>
    /// <param name="source">Component supplied by the game, if available</param>
    /// <param name="isTrace">Whether this is a stack trace line</param>
    void Append(string instanceId, string level, string text,
        DateTimeOffset? timestamp = null, string? source = null, bool isTrace = false);

    /// <summary>
    /// Gets the retained console lines for an instance in capture order
    /// </summary>
    /// <param name="instanceId">Stable instance identifier</param>
    /// <returns>A snapshot of buffered lines; empty when nothing was captured yet</returns>
    IReadOnlyList<GameConsoleLine> GetLines(string instanceId);

    /// <summary>
    /// Drops all retained console lines for an instance
    /// </summary>
    /// <param name="instanceId">Stable instance identifier</param>
    void Clear(string instanceId);
}

/// <summary>
/// In-memory per-instance console ring buffer with bounded retention
/// </summary>
public sealed class GameConsoleService : IGameConsoleService
{
    private const int MaxLinesPerInstance = 4000;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<GameConsoleLine>> _buffers =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public event EventHandler<GameConsoleLineEventArgs>? LineReceived;

    /// <inheritdoc/>
    public void Append(string instanceId, string level, string text,
        DateTimeOffset? timestamp = null, string? source = null, bool isTrace = false)
    {
        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(text))
            return;

        var normalizedLevel = GameLogParser.NormalizeLevel(level);
        var line = new GameConsoleLine(instanceId, normalizedLevel, text,
            timestamp ?? DateTimeOffset.Now, source ?? "Game", isTrace || normalizedLevel == "TRACE");
        var buffer = _buffers.GetOrAdd(instanceId, _ => new ConcurrentQueue<GameConsoleLine>());
        buffer.Enqueue(line);
        while (buffer.Count > MaxLinesPerInstance && buffer.TryDequeue(out _))
        {
        }

        try
        {
            LineReceived?.Invoke(this, new GameConsoleLineEventArgs(line));
        }
        catch (Exception ex)
        {
            Logger.Warning("GameConsole", $"Console subscriber failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<GameConsoleLine> GetLines(string instanceId)
        => _buffers.TryGetValue(instanceId, out var buffer)
            ? buffer.ToArray()
            : [];

    /// <inheritdoc/>
    public void Clear(string instanceId)
    {
        if (_buffers.TryGetValue(instanceId, out var buffer))
            while (buffer.TryDequeue(out _))
            {
            }
    }
}
