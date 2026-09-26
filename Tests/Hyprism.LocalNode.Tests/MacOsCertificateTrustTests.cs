// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography.X509Certificates;
using Hyprism.LocalNode;

namespace Hyprism.LocalNode.Tests;

public sealed class MacOsCertificateTrustTests
{
    [Fact]
    public void EnsureTrusted_AlreadyTrusted_DoesNotInstallAgain()
    {
        using var fixture = CertificateFixture.Create();
        var trustedCopy = Path.Combine(fixture.Options.DataDirectory, "macos-trusted-v2.crt");
        File.Copy(LocalNodeCertificateStore.GetRootPublicCertificatePath(fixture.Options), trustedCopy);
        var commands = new List<string[]>();

        MacOsCertificateTrust.EnsureTrusted(
            fixture.Options,
            fixture.Certificate,
            fixture.RootCertificate,
            arguments =>
            {
                commands.Add(arguments.ToArray());
                return new MacOsTrustCommandResult(0);
            });

        var command = Assert.Single(commands);
        Assert.Equal("verify-cert", command[0]);
        Assert.Equal(fixture.Options.Hostname, ArgumentAfter(command, "-s"));
        Assert.True(File.Exists(trustedCopy));
    }

    [Fact]
    public void EnsureTrusted_FirstInstall_UsesAdminDomainTrustRoot()
    {
        using var fixture = CertificateFixture.Create();
        var commands = new List<string[]>();

        MacOsCertificateTrust.EnsureTrusted(
            fixture.Options,
            fixture.Certificate,
            fixture.RootCertificate,
            arguments =>
            {
                commands.Add(arguments.ToArray());
                return new MacOsTrustCommandResult(0);
            });

        Assert.Equal(
            ["remove-trusted-cert", "remove-trusted-cert", "add-trusted-cert", "verify-cert"],
            commands.Select(item => item[0]));
        var install = commands[2];
        Assert.Equal("add-trusted-cert", install[0]);
        Assert.Contains("-d", install);
        Assert.Equal("trustRoot", ArgumentAfter(install, "-r"));
        Assert.EndsWith("login.keychain-db", ArgumentAfter(install, "-k"), StringComparison.Ordinal);
        Assert.Equal(
            LocalNodeCertificateStore.GetRootPublicCertificatePath(fixture.Options),
            install[^1]);
        Assert.True(File.Exists(Path.Combine(fixture.Options.DataDirectory, "macos-trusted-v2.crt")));
        Assert.False(File.Exists(Path.Combine(fixture.Options.DataDirectory, "macos-trusted.crt")));
    }

    [Fact]
    public void EnsureTrusted_RotatedCertificate_RemovesOldTrustBeforeInstall()
    {
        using var oldFixture = CertificateFixture.Create();
        using var currentFixture = CertificateFixture.Create();
        var legacyTrustedCopy = Path.Combine(currentFixture.Options.DataDirectory, "macos-trusted.crt");
        File.Copy(LocalNodeCertificateStore.GetRootPublicCertificatePath(oldFixture.Options), legacyTrustedCopy);
        var commands = new List<string[]>();

        MacOsCertificateTrust.EnsureTrusted(
            currentFixture.Options,
            currentFixture.Certificate,
            currentFixture.RootCertificate,
            arguments =>
            {
                commands.Add(arguments.ToArray());
                return new MacOsTrustCommandResult(0);
            });

        Assert.Equal(
            [
                "remove-trusted-cert",
                "remove-trusted-cert",
                "delete-certificate",
                "remove-trusted-cert",
                "remove-trusted-cert",
                "add-trusted-cert",
                "verify-cert"
            ],
            commands.Select(item => item[0]));

        var markerPath = Path.Combine(currentFixture.Options.DataDirectory, "macos-trusted-v2.crt");
        using var preserved = X509CertificateLoader.LoadCertificateFromFile(markerPath);
        Assert.Equal(currentFixture.RootCertificate.Thumbprint, preserved.Thumbprint);
        Assert.False(File.Exists(legacyTrustedCopy));
    }

    [Fact]
    public void EnsureTrusted_InstallRejected_ReportsActionableError()
    {
        using var fixture = CertificateFixture.Create();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MacOsCertificateTrust.EnsureTrusted(
                fixture.Options,
                fixture.Certificate,
                fixture.RootCertificate,
                arguments => arguments[0] == "verify-cert"
                    ? new MacOsTrustCommandResult(1)
                    : new MacOsTrustCommandResult(1, StandardError: "User canceled")));

        Assert.Contains("Allow the security prompt and retry", exception.Message);
        Assert.Contains("User canceled", exception.Message);
    }

    private static string ArgumentAfter(IReadOnlyList<string> arguments, string name)
    {
        var index = arguments.ToList().IndexOf(name);
        Assert.InRange(index, 0, arguments.Count - 2);
        return arguments[index + 1];
    }

    private sealed class CertificateFixture : IDisposable
    {
        private CertificateFixture(
            string directory,
            LocalNodeOptions options,
            X509Certificate2 certificate,
            X509Certificate2 rootCertificate)
        {
            Directory = directory;
            Options = options;
            Certificate = certificate;
            RootCertificate = rootCertificate;
        }

        public string Directory { get; }

        public LocalNodeOptions Options { get; }

        public X509Certificate2 Certificate { get; }

        public X509Certificate2 RootCertificate { get; }

        public static CertificateFixture Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "HyprismMacTrustTests_" + Guid.NewGuid());
            var options = new LocalNodeOptions(directory, "127.0.0.1", 8443);
            var certificate = LocalNodeCertificateStore.LoadOrCreate(options);
            var rootCertificate = LocalNodeCertificateStore.LoadRootCertificate(options);
            return new CertificateFixture(directory, options, certificate, rootCertificate);
        }

        public void Dispose()
        {
            Certificate.Dispose();
            RootCertificate.Dispose();
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
