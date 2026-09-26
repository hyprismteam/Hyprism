// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Models;

namespace Hyprism.Core.Tests.Game.Mods;

public sealed class ModCompatibilityEvaluatorTests : IDisposable
{
    private readonly string _instancePath = Path.Combine(
        Path.GetTempPath(),
        $"HyprismModCompatibilityTests_{Guid.NewGuid():N}");

    [Fact]
    public void DetectInstanceGameVersion_ReadsServerManifest()
    {
        var serverPath = Path.Combine(_instancePath, "Server");
        Directory.CreateDirectory(serverPath);
        using (var archive = ZipFile.Open(Path.Combine(serverPath, "HytaleServer.jar"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open()))
            writer.WriteLine("Implementation-Version: 0.6.0-pre.11");

        Assert.Equal("0.6.0-pre.11", ModCompatibilityEvaluator.DetectInstanceGameVersion(_instancePath));
    }

    [Theory]
    [InlineData("0.6.0-pre.11", "0.6", ModCompatibilityStatus.Compatible)]
    [InlineData("0.6.0-pre.11", "0.5", ModCompatibilityStatus.Incompatible)]
    [InlineData("0.6.0-pre.11", "Early Access", ModCompatibilityStatus.Unknown)]
    [InlineData(null, "0.6", ModCompatibilityStatus.Unknown)]
    public void Evaluate_UsesMajorMinorTags(
        string? instanceVersion,
        string fileVersion,
        ModCompatibilityStatus expected)
        => Assert.Equal(expected, ModCompatibilityEvaluator.Evaluate(instanceVersion, [fileVersion]));

    [Fact]
    public void SelectRecommendedFile_PrefersCompatibleFileOverUnknownFallback()
    {
        var unknown = new ModFileInfo { Id = "unknown", GameVersions = ["Early Access"] };
        var compatible = new ModFileInfo { Id = "compatible", GameVersions = ["0.6"] };

        Assert.Same(
            compatible,
            ModCompatibilityEvaluator.SelectRecommendedFile([unknown, compatible], "0.6.0-pre.11"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_instancePath))
            Directory.Delete(_instancePath, true);
    }
}
