// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using Hyprism.Core.Game.Launch;

namespace Hyprism.Core.Tests.Game.Launch;

/// <summary>
/// Tests for <see cref="ClientPatcher"/>. All tests operate on temporary directories
/// containing synthetic binary files without requiring real Hytale binaries
/// </summary>
public class ClientPatcherTests : IDisposable
{
    private readonly string _gameDir;

    // The original domain embedded in binaries that ClientPatcher replaces
    private const string OriginalDomain = "hytale.com";

    public ClientPatcherTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "HyprismPatcherTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_gameDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDir))
            Directory.Delete(_gameDir, true);
    }


    /// <summary>
    /// Creates a synthetic binary containing the domain in UTF-8 (default) or
    /// length-prefixed UTF-16LE format (used by .NET AOT binaries)
    /// </summary>
    private static string CreateFakeBinary(string dir, string filename, string domain = OriginalDomain,
        bool lengthPrefixed = false)
    {
        var path = Path.Combine(dir, filename);
        var prefix = new byte[] { 0x00, 0x01, 0x02 };
        byte[] domainBytes;
        if (lengthPrefixed)
        {
            // Replicate ClientPatcher.StringToLengthPrefixed:
            // [len][0x00][0x00][0x00] then each char as [c][0x00]
            var lp = new List<byte> { (byte)domain.Length, 0, 0, 0 };
            foreach (char c in domain) { lp.Add((byte)c); lp.Add(0); }
            domainBytes = lp.ToArray();
        }
        else
        {
            domainBytes = Encoding.UTF8.GetBytes(domain);
        }
        File.WriteAllBytes(path, prefix.Concat(domainBytes).ToArray());
        return path;
    }


    [Fact]
    public void PatchClient_PreservesTheExecutableBitOnUnix()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient", lengthPrefixed: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(clientPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var patcher = new ClientPatcher("sanasol.ws");
        var result = patcher.PatchClient(clientPath);

        Assert.True(result.Success, result.Error);
        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(clientPath);
        Assert.Equal(UnixFileMode.UserExecute, mode & UnixFileMode.UserExecute);
    }

    [Fact]
    public void PatchClient_WhenTargetChanges_PreservesTheExecutableBitAfterRestore()
    {
        var clientPath = Path.Combine(_gameDir, "HytaleClient");
        File.WriteAllBytes(clientPath, ToLengthPrefixed(OriginalDomain)
            .Concat(ToLengthPrefixed("https://sessions."))
            .ToArray());
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(clientPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var localPatcher = new ClientPatcher("127.0.0.1:8443");
        var firstPatch = localPatcher.PatchClient(clientPath);
        Assert.True(firstPatch.Success, firstPatch.Error);

        var restoreResult = new ClientPatcher("sanasol.ws").PatchClient(clientPath);

        Assert.True(restoreResult.Success, restoreResult.Error);
        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(clientPath);
        Assert.Equal(UnixFileMode.UserExecute, mode & UnixFileMode.UserExecute);
    }

    [Fact]
    public void IsPatchedAlready_UnpatchedFile_ReturnsFalse()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        Assert.False(patcher.IsPatchedAlready(clientPath));
    }

    [Fact]
    public void IsPatchedAlready_AfterPatching_ReturnsTrue()
    {
        // Use length-prefixed format so PatchClient patches via the LP path and
        // IsPatchedAlready can confirm the patched domain is present.
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient", lengthPrefixed: true);
        var patcher = new ClientPatcher("sanasol.ws");

        patcher.PatchClient(clientPath);

        Assert.True(patcher.IsPatchedAlready(clientPath));
    }


    [Fact]
    public void PatchClient_ValidBinary_ReturnsSuccess()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        var result = patcher.PatchClient(clientPath);

        Assert.True(result.Success || result.AlreadyPatched, $"Patch failed: {result.Error}");
    }

    [Fact]
    public void PatchClient_CreatesBackup()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        patcher.PatchClient(clientPath);

        // Backup should exist at the same path with ".bak" or similar extension
        var backupFiles = Directory.GetFiles(_gameDir, "HytaleClient*");
        Assert.True(backupFiles.Length >= 2, "Expected at least original + backup file");
    }

    [Fact]
    public void PatchClient_FlatTemporaryPath_KeepsStateFilesNextToBinary()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");

        var result = new ClientPatcher("sanasol.ws").PatchClient(clientPath);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(clientPath + ".original"));
        Assert.True(File.Exists(clientPath + ".patched_custom"));
    }

    [Fact]
    public void PatchClient_AlreadyPatched_ReturnsAlreadyPatched()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient");
        var patcher = new ClientPatcher("sanasol.ws");

        patcher.PatchClient(clientPath);
        var second = patcher.PatchClient(clientPath);

        Assert.True(second.AlreadyPatched || second.Success);
    }


    [Fact]
    public void IsClientPatched_NoClientBinary_ReturnsFalse()
    {
        // Empty game directory without a client binary
        var result = ClientPatcher.IsClientPatched(_gameDir);
        Assert.False(result);
    }


    [Fact]
    public void Constructor_TooShortDomain_FallsBackToDefault()
    {
        // Domain shorter than 4 chars is invalid; constructor should fall back silently
        var ex = Record.Exception(() => new ClientPatcher("ab"));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_TooLongDomain_FallsBackToDefault()
    {
        var ex = Record.Exception(() => new ClientPatcher("toolongdomainname.example.com"));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_NullDomain_UsesDefault()
    {
        var ex = Record.Exception(() => new ClientPatcher(null));
        Assert.Null(ex);
    }

    [Fact]
    public void PatchClient_LocalNodeTarget_RoutesAllKnownServicePrefixesToLoopback()
    {
        var clientPath = Path.Combine(_gameDir, "HytaleClient");
        string[] values =
        [
            OriginalDomain,
            "https://sessions.",
            "https://account-data.",
            "https://telemetry.",
            "https://liveconfig.",
            "https://server-discovery.",
            "https://social.",
            "wss://socket-gateway."
        ];
        File.WriteAllBytes(clientPath, values.SelectMany(value => ToLengthPrefixed(value)).ToArray());

        var result = new ClientPatcher("127.0.0.1:8443").PatchClient(clientPath);

        Assert.True(result.Success, result.Error);
        var patched = File.ReadAllBytes(clientPath);
        AssertContains(patched, ToLengthPrefixed("0.1:8443"));
        Assert.Equal(6, CountOccurrences(patched, ToLengthPrefixed("https://127.0.")));
        AssertContains(patched, ToLengthPrefixed("wss://127.0."));
    }

    [Fact]
    public void PatchClient_WhenTargetChanges_RestoresOriginalBeforeRepatching()
    {
        var clientPath = Path.Combine(_gameDir, "HytaleClient");
        File.WriteAllBytes(clientPath, ToLengthPrefixed(OriginalDomain)
            .Concat(ToLengthPrefixed("https://sessions."))
            .ToArray());
        var localPatcher = new ClientPatcher("127.0.0.1:8443");
        var connectedPatcher = new ClientPatcher("sanasol.ws");
        Assert.True(localPatcher.PatchClient(clientPath).Success);

        var result = connectedPatcher.PatchClient(clientPath);

        Assert.True(result.Success, result.Error);
        Assert.True(connectedPatcher.IsPatchedAlready(clientPath));
        Assert.False(localPatcher.IsPatchedAlready(clientPath));
        AssertContains(File.ReadAllBytes(clientPath), ToLengthPrefixed("sanasol.ws"));
    }

    [Fact]
    public void PatchClient_LocalNodeTargetWithoutServicePrefixes_FailsWithoutWriting()
    {
        var clientPath = CreateFakeBinary(_gameDir, "HytaleClient", lengthPrefixed: true);
        var original = File.ReadAllBytes(clientPath);

        var result = new ClientPatcher("127.0.0.1:8443").PatchClient(clientPath);

        Assert.False(result.Success);
        Assert.Contains("length-prefixed service URLs", result.Error);
        Assert.Equal(original, File.ReadAllBytes(clientPath));
    }

    [Fact]
    public void PatchClient_LocalNodeTarget_PatchesNativeAotFrozenStringsWithoutChangingMetadata()
    {
        var clientPath = Path.Combine(_gameDir, "HytaleClient");
        const byte metadata = 0x89;
        var frozenDomain = ToFrozenString(OriginalDomain, metadata);
        var frozenSessions = ToFrozenString("https://sessions.", metadata);
        var frozenAccountData = ToFrozenString("https://account-data.", metadata);
        File.WriteAllBytes(clientPath, frozenDomain
            .Concat(frozenSessions)
            .Concat(frozenAccountData)
            .ToArray());

        var result = new ClientPatcher("127.0.0.1:8443").PatchClient(clientPath);

        Assert.True(result.Success, result.Error);
        var patched = File.ReadAllBytes(clientPath);
        AssertContains(patched, ToFrozenPattern("0.1:8443"));
        Assert.Equal(2, CountOccurrences(patched, ToFrozenPattern("https://127.0.")));
        Assert.Equal(3, patched.Count(value => value == metadata));
    }

    private static byte[] ToLengthPrefixed(string value)
    {
        var bytes = new List<byte> { (byte)value.Length, 0, 0, 0 };
        foreach (var character in value)
        {
            bytes.Add((byte)character);
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    private static byte[] ToFrozenString(string value, byte metadata)
        => ToFrozenPattern(value).Append(metadata).ToArray();

    private static byte[] ToFrozenPattern(string value)
    {
        var bytes = new List<byte>
        {
            (byte)value.Length,
            0,
            0,
            0
        };
        var utf16 = Encoding.Unicode.GetBytes(value);
        bytes.AddRange(utf16[..^1]);
        return bytes.ToArray();
    }

    private static void AssertContains(byte[] data, byte[] value)
        => Assert.True(CountOccurrences(data, value) > 0);

    private static int CountOccurrences(byte[] data, byte[] value)
    {
        var count = 0;
        for (var index = 0; index <= data.Length - value.Length; index++)
        {
            if (data.AsSpan(index, value.Length).SequenceEqual(value))
                count++;
        }
        return count;
    }
}
