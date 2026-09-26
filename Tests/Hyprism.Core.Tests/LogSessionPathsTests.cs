// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Infrastructure;

namespace Hyprism.Core.Tests;

public sealed class LogSessionPathsTests
{
    [Fact]
    public void SessionPaths_GroupLauncherInstancesAndNodesByLauncherStartTime()
    {
        var appDirectory = Path.Combine(Path.GetTempPath(), "HyprismLogSessionTests_" + Guid.NewGuid());
        try
        {
            var startedAt = new DateTimeOffset(2026, 8, 15, 20, 31, 42, 137, TimeSpan.FromHours(3));
            var paths = new LogSessionPaths(appDirectory, startedAt);

            Assert.Equal(
                Path.Combine(appDirectory, "Logs", "2026-08-15_20-31-42.137"),
                paths.SessionDirectory);
            Assert.Equal(Path.Combine(paths.SessionDirectory, "launcher.log"), paths.LauncherLogPath);
            Assert.Equal(
                Path.Combine(paths.SessionDirectory, "instance-release_main.log"),
                paths.GetInstanceLogPath("release/main"));
            Assert.Equal(
                Path.Combine(paths.SessionDirectory, "local-node-8443.log"),
                paths.GetLocalNodeLogPath(8443));
            Assert.Equal(
                Path.Combine(paths.SessionDirectory, "local-node-requests-8443.ndjson"),
                paths.GetLocalNodeRequestJournalPath(8443));
            Assert.True(Directory.Exists(paths.SessionDirectory));
        }
        finally
        {
            if (Directory.Exists(appDirectory))
                Directory.Delete(appDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SessionLogWriter_AppendsTimestampedSourceRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HyprismSessionLogWriterTests_" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(directory, "instance-test.log");
            var writer = new SessionLogWriter(path);

            writer.Write("OUT", "Game", "client output");
            writer.Write("ERR", "Game", "client error");

            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("|INFO|Game|client output", content);
            Assert.Contains("|ERROR|Game|client error", content);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LauncherLogger_WritesToCurrentSessionFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HyprismLauncherLogTests_" + Guid.NewGuid());
        var path = Path.Combine(directory, "launcher.log");
        try
        {
            Logger.ConfigureFileLogging(path);
            Logger.Info("LogSessionTest", "central launcher record", logToConsole: false);
            Logger.Debug("LogSessionTest", "debug launcher record");
            Logger.Shutdown();

            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("[LogSessionTest]", content);
            Assert.Contains("central launcher record", content);
            Assert.Contains("debug launcher record", content);
        }
        finally
        {
            Logger.Shutdown();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
