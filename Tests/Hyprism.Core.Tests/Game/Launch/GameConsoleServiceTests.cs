// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Game.Launch;

namespace Hyprism.Core.Tests.Game.Launch;

public sealed class GameConsoleServiceTests
{
    [Fact]
    public void Append_RaisesLineReceived_AndKeepsHistoryInOrder()
    {
        var console = new GameConsoleService();
        var received = new List<GameConsoleLine>();
        console.LineReceived += (_, args) => received.Add(args.Line);

        console.Append("instance-a", "OUT", "first");
        console.Append("instance-a", "ERR", "second");
        console.Append("instance-b", "OUT", "other instance");

        Assert.Equal(3, received.Count);
        var lines = console.GetLines("instance-a");
        Assert.Equal(["first", "second"], lines.Select(line => line.Text));
        Assert.Equal(["INFO", "ERROR"], lines.Select(line => line.Level));
        Assert.Single(console.GetLines("instance-b"));
    }

    [Fact]
    public void Append_TrimsOldestLinesBeyondRetentionCap()
    {
        var console = new GameConsoleService();

        for (var index = 0; index < 4500; index++)
            console.Append("instance-a", "OUT", $"line {index}");

        var lines = console.GetLines("instance-a");
        Assert.Equal(4000, lines.Count);
        Assert.Equal("line 500", lines[0].Text);
        Assert.Equal("line 4499", lines[^1].Text);
    }

    [Fact]
    public void Clear_RemovesOnlyTheRequestedInstance()
    {
        var console = new GameConsoleService();
        console.Append("instance-a", "OUT", "first");
        console.Append("instance-b", "OUT", "second");

        console.Clear("instance-a");

        Assert.Empty(console.GetLines("instance-a"));
        Assert.Single(console.GetLines("instance-b"));
    }

    [Fact]
    public void Append_IgnoresEmptyText()
    {
        var console = new GameConsoleService();
        var raised = 0;
        console.LineReceived += (_, _) => raised++;

        console.Append("instance-a", "OUT", string.Empty);
        console.Append(string.Empty, "OUT", "text");

        Assert.Equal(0, raised);
        Assert.Empty(console.GetLines("instance-a"));
    }
}
