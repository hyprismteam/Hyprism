// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Game.Launch;

/// <summary>
/// Tracks every game process started by Hyprism and restores still-live entries after restart.
/// Persisted entries are verified by PID and process start time before they are trusted, which
/// prevents stale files and PID reuse from being reported as running games.
/// </summary>
public sealed class GameProcessTracker : IGameProcessTracker, IDisposable
{
    private const string RegistryFileName = "GameProcesses.json";
    private static readonly TimeSpan ProcessStartTimeTolerance = TimeSpan.FromMilliseconds(100);
    private readonly Lock _processLock = new();
    private readonly Dictionary<int, TrackedProcess> _processes = [];
    private readonly List<GameProcessInfo> _processesExitedWhileUnavailable = [];
    private readonly string? _registryPath;
    private bool _disposed;

    /// <summary>
    /// Creates an in-memory tracker. This overload is intended for tests and hosts without app storage.
    /// </summary>
    public GameProcessTracker()
    {
    }

    /// <summary>
    /// Creates a tracker backed by the launcher runtime registry.
    /// </summary>
    public GameProcessTracker(AppPathConfiguration appPath)
    {
        ArgumentNullException.ThrowIfNull(appPath);
        _registryPath = LauncherJsonFile.GetPath(
            Path.Combine(appPath.AppDir, "Runtime"),
            RegistryFileName,
            "game-processes.json");
        RestoreTrackedProcesses();
    }

    /// <inheritdoc/>
    public event EventHandler<GameProcessStartedEventArgs>? GameProcessStarted;

    /// <inheritdoc/>
    public event EventHandler<GameProcessExitedEventArgs>? GameProcessExited;

    /// <inheritdoc/>
    public void TrackGameProcess(
        Process process,
        string instanceId,
        string profileId,
        string? officialAccountId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var info = new GameProcessInfo(
            process.Id,
            GetProcessStartTimeUtc(process),
            instanceId,
            profileId,
            officialAccountId,
            DateTime.UtcNow);

        lock (_processLock)
        {
            RemoveProcessLocked(process.Id, disposeProcess: false);
            process.EnableRaisingEvents = true;
            process.Exited += OnGameProcessExited;
            _processes.Add(process.Id, new TrackedProcess(process, info));
            SaveRegistryLocked();
        }

        GameProcessStarted?.Invoke(this, new GameProcessStartedEventArgs(info));
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<GameProcessInfo> GetRunningProcesses()
    {
        lock (_processLock)
        {
            RestoreProcessesTrackedByOtherLaunchersLocked();
            RemoveExitedProcessesLocked();
            return [.. _processes.Values
                .Select(tracked => tracked.Info)
                .OrderBy(tracked => tracked.RegisteredAtUtc)];
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<GameProcessInfo> TakeProcessesExitedWhileUnavailable()
    {
        lock (_processLock)
        {
            var exited = _processesExitedWhileUnavailable.ToArray();
            _processesExitedWhileUnavailable.Clear();
            return exited;
        }
    }

    /// <inheritdoc/>
    public bool IsInstanceRunning(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_processLock)
        {
            RestoreProcessesTrackedByOtherLaunchersLocked();
            RemoveExitedProcessesLocked();
            return _processes.Values.Any(tracked =>
                string.Equals(tracked.Info.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc/>
    public bool IsGameRunning()
    {
        lock (_processLock)
        {
            RestoreProcessesTrackedByOtherLaunchersLocked();
            RemoveExitedProcessesLocked();
            return _processes.Count > 0;
        }
    }

    /// <inheritdoc/>
    public bool ExitGame(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        Process? process;
        lock (_processLock)
        {
            RestoreProcessesTrackedByOtherLaunchersLocked();
            RemoveExitedProcessesLocked();
            process = _processes.Values
                .Where(tracked => string.Equals(
                    tracked.Info.InstanceId,
                    instanceId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(tracked => tracked.Info.RegisteredAtUtc)
                .Select(tracked => tracked.Process)
                .FirstOrDefault(IsAlive);
        }

        return StopProcess(process);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_processLock)
        {
            foreach (var tracked in _processes.Values)
            {
                tracked.Process.Exited -= OnGameProcessExited;
                tracked.Process.Dispose();
            }

            _processes.Clear();
            _disposed = true;
        }
    }

    private void RestoreTrackedProcesses()
    {
        if (string.IsNullOrWhiteSpace(_registryPath) || !File.Exists(_registryPath))
            return;

        try
        {
            var records = JsonSerializer.Deserialize<List<GameProcessInfo>>(
                File.ReadAllText(_registryPath),
                JsonDefaults.CaseInsensitive) ?? [];
            foreach (var record in records)
            {
                var process = TryRestoreProcess(record);
                if (process is null)
                {
                    _processesExitedWhileUnavailable.Add(record);
                    continue;
                }

                process.EnableRaisingEvents = true;
                process.Exited += OnGameProcessExited;
                _processes[process.Id] = new TrackedProcess(process, record);
            }

            lock (_processLock)
                SaveRegistryLocked();

            if (_processes.Count > 0)
                Logger.Info("Game", $"Restored {_processes.Count} running game process(es) from the launch registry");
        }
        catch (Exception exception)
        {
            Logger.Warning("Game", $"Could not restore the game process registry: {exception.Message}");
        }
    }

    private static Process? TryRestoreProcess(GameProcessInfo record)
    {
        try
        {
            var process = Process.GetProcessById(record.ProcessId);
            if (!IsAlive(process) || !HasMatchingStartTime(process, record))
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private void OnGameProcessExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not Process exitedProcess)
            return;

        GameProcessInfo? info;
        int exitCode;
        lock (_processLock)
        {
            if (!_processes.TryGetValue(exitedProcess.Id, out var tracked)
                || !ReferenceEquals(tracked.Process, exitedProcess))
            {
                return;
            }

            info = tracked.Info;
            exitCode = ReadExitCode(exitedProcess);
            RemoveProcessLocked(exitedProcess.Id, disposeProcess: true);
            SaveRegistryLocked();
        }

        GameProcessExited?.Invoke(this, new GameProcessExitedEventArgs(info, exitCode));
    }

    private static int ReadExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return 0;
        }
    }

    private void RemoveExitedProcessesLocked()
    {
        var exited = _processes.Values
            .Where(tracked => !IsAlive(tracked.Process))
            .Select(tracked => tracked.Process.Id)
            .ToArray();
        if (exited.Length == 0)
            return;

        foreach (var processId in exited)
            RemoveProcessLocked(processId, disposeProcess: true);
        SaveRegistryLocked();
    }

    private void RemoveProcessLocked(int processId, bool disposeProcess)
    {
        if (!_processes.Remove(processId, out var tracked))
            return;

        tracked.Process.Exited -= OnGameProcessExited;
        if (disposeProcess)
            tracked.Process.Dispose();
    }

    private void SaveRegistryLocked()
    {
        if (string.IsNullOrWhiteSpace(_registryPath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(_registryPath)!;
            Directory.CreateDirectory(directory);
            using var registryLock = AcquireRegistryWriteLock();
            if (registryLock is null)
            {
                Logger.Warning("Game", "Could not acquire the game process registry lock");
                return;
            }

            var persistedLiveRecords = ReadRegistryRecords()
                .Where(IsRecordAlive)
                .ToList();
            var records = persistedLiveRecords
                .Concat(_processes.Values.Select(tracked => tracked.Info))
                .GroupBy(record => record.ProcessId)
                .Select(group => group.Last())
                .ToArray();
            var temporaryPath = $"{_registryPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            var content = JsonSerializer.Serialize(
                records,
                JsonDefaults.Indented);
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, _registryPath, overwrite: true);
        }
        catch (Exception exception)
        {
            Logger.Warning("Game", $"Could not save the game process registry: {exception.Message}");
        }
    }

    private void RestoreProcessesTrackedByOtherLaunchersLocked()
    {
        if (string.IsNullOrWhiteSpace(_registryPath))
            return;

        foreach (var record in ReadRegistryRecords())
        {
            if (_processes.ContainsKey(record.ProcessId))
                continue;

            var process = TryRestoreProcess(record);
            if (process is null)
                continue;

            process.EnableRaisingEvents = true;
            process.Exited += OnGameProcessExited;
            _processes.Add(process.Id, new TrackedProcess(process, record));
        }
    }

    private List<GameProcessInfo> ReadRegistryRecords()
    {
        if (string.IsNullOrWhiteSpace(_registryPath) || !File.Exists(_registryPath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<GameProcessInfo>>(
                File.ReadAllText(_registryPath),
                JsonDefaults.CaseInsensitive) ?? [];
        }
        catch (Exception exception)
        {
            Logger.Warning("Game", $"Could not read the game process registry: {exception.Message}");
            return [];
        }
    }

    private FileStream? AcquireRegistryWriteLock()
    {
        if (string.IsNullOrWhiteSpace(_registryPath))
            return null;

        var lockPath = _registryPath + ".lock";
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 39)
            {
                Thread.Sleep(25);
            }
        }

        return null;
    }

    private static bool IsRecordAlive(GameProcessInfo record)
    {
        var process = TryGetProcess(record.ProcessId);
        if (process is null)
            return false;

        using (process)
        {
            try
            {
                return IsAlive(process) && HasMatchingStartTime(process, record);
            }
            catch
            {
                return false;
            }
        }
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static DateTime GetProcessStartTimeUtc(Process process)
        => process.StartTime.ToUniversalTime();

    private static bool HasMatchingStartTime(Process process, GameProcessInfo record)
        => Math.Abs((GetProcessStartTimeUtc(process) - record.ProcessStartedAtUtc).Ticks)
           <= ProcessStartTimeTolerance.Ticks;

    private static bool IsAlive(Process? process)
    {
        if (process is null)
            return false;

        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool StopProcess(Process? process)
    {
        if (!IsAlive(process))
            return false;

        try
        {
            process!.Kill(entireProcessTree: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private sealed record TrackedProcess(Process Process, GameProcessInfo Info);
}
