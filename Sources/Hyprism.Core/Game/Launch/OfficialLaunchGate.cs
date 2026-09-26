// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Core.Game.Launch;

/// <summary>
/// Serializes official authentication and process registration across launcher processes.
/// The lock is short lived: it prevents a concurrent launch from missing a just-created
/// registry entry, while the persistent process registry protects the running lifetime.
/// </summary>
internal sealed class OfficialLaunchGate : IDisposable
{
    private readonly Mutex _mutex = new(false, "Hyprism.OfficialLaunchGate");
    private bool _entered;

    public void Enter()
    {
        try
        {
            _entered = _mutex.WaitOne(TimeSpan.FromMinutes(2));
        }
        catch (AbandonedMutexException)
        {
            _entered = true;
        }

        if (!_entered)
        {
            throw new TimeoutException(
                "Timed out waiting for another official Hytale launch to finish authentication");
        }
    }

    public void Dispose()
    {
        if (_entered)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
