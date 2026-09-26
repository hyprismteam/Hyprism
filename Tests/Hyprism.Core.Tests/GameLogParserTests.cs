// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Game.Launch;
using Hyprism.Core.Infrastructure;
using Xunit;

namespace Hyprism.Core.Tests;

public sealed class GameLogParserTests
{
    [Fact]
    public void ParsesHytaleFieldsAndAssociatesExceptionAndStackLines()
    {
        var parser = new GameLogParser();
        var error = parser.Parse(
            "2026-09-25 13:14:03.3746|ERROR|HytaleClient.Application.Program|Failed to fetch live config");
        var exception = parser.Parse("System.Text.Json.JsonException: JSON deserialization failed");
        var trace = parser.Parse("   at HytaleClient<BaseAddress>+0x13c54d8");

        Assert.Equal("ERROR", error.Level);
        Assert.Equal("HytaleClient.Application.Program", error.Source);
        Assert.Equal("Failed to fetch live config", error.Message);
        Assert.Equal("3746", error.Timestamp.ToString("ffff"));
        Assert.Equal(error.Timestamp, exception.Timestamp);
        Assert.Equal(error.Source, exception.Source);
        Assert.Equal("ERROR", exception.Level);
        Assert.True(trace.IsTrace);
        Assert.Equal("TRACE", trace.Level);
        Assert.Equal(error.Timestamp, trace.Timestamp);
    }

    [Fact]
    public async Task WritesNormalizedRecordsWithoutDuplicatingClientHeader()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HyprismGameLogTests_" + Guid.NewGuid());
        try
        {
            var parser = new GameLogParser();
            var writer = new SessionLogWriter(Path.Combine(directory, "instance.log"));
            var entry = parser.Parse(
                "2026-09-25 13:11:20.2426|INFO|HytaleClient.Application.AppStartup|Loading assets");
            writer.Write(entry.Timestamp, entry.Level, entry.Source, entry.Message);
            var content = await File.ReadAllTextAsync(writer.FilePath);

            Assert.Equal(
                "2026-09-25 13:11:20.2426|INFO|HytaleClient.Application.AppStartup|Loading assets" +
                Environment.NewLine, content);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
