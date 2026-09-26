// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using Hyprism.Desktop.Screens.Settings;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class InstanceDirectoryCopyTests
{
    [Fact]
    public async Task CopyFileAsync_PreservesTheExecutableBitOnUnix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var directory = Path.Combine(Path.GetTempPath(), "HyprismInstanceCopyTests_" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "HytaleClient");
            var destinationDirectory = Path.Combine(directory, "Copied");
            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, "HytaleClient");
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
            File.SetUnixFileMode(source,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            await DesktopSettingsStore.CopyFileAsync(source, destination, CancellationToken.None, _ => { });

            Assert.True(File.Exists(destination));
            var mode = File.GetUnixFileMode(destination);
            Assert.Equal(UnixFileMode.UserExecute, mode & UnixFileMode.UserExecute);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
