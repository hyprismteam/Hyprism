// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Hyprism.LocalNode;

/// <summary>
/// Persists the Local Node certificate authority and its loopback server certificate
/// </summary>
public static class LocalNodeCertificateStore
{
    private const string RootCertificateFileName = "hyprism-local-ca.pfx";
    private const string RootPublicCertificateFileName = "hyprism-local-ca.crt";
    private const string CertificateFileName = "h.localhost.pfx";
    private const string PublicCertificateFileName = "h.localhost.crt";
    private const string RootSubject = "CN=Hyprism Local Node Root CA";
    private static readonly object CertificateLock = new();
    private const X509KeyStorageFlags EphemeralKeyStorageFlags =
        X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;

    /// <summary>
    /// Windows Schannel server credentials and the macOS keychain require an imported PKCS#12 key
    /// </summary>
    private static X509KeyStorageFlags KeyStorageFlags
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.Exportable
            : EphemeralKeyStorageFlags;

    /// <summary>
    /// Loads the persistent h.localhost server certificate or creates it on first use
    /// </summary>
    /// <param name="options">Endpoint and storage options for the Local Node</param>
    /// <returns>A certificate containing an exportable private key for Kestrel</returns>
    /// <exception cref="CryptographicException">Thrown when the certificate cannot be generated or loaded</exception>
    public static X509Certificate2 LoadOrCreate(LocalNodeOptions options)
    {
        lock (CertificateLock)
        {
            Directory.CreateDirectory(GetCertificateDirectory(options));
            using var rootCertificate = LoadOrCreateRootCertificate(options);
            var certificatePath = GetCertificatePath(options);

            if (TryLoadCurrentServerCertificate(certificatePath, options.Hostname, rootCertificate, out var currentCertificate))
            {
                EnsurePublicCertificate(options, currentCertificate);
                return currentCertificate;
            }

            PreserveInvalidCertificate(certificatePath);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                $"CN={options.Hostname}",
                key,
                HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")],
                false));

            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            if (IPAddress.TryParse(options.Hostname, out var hostnameAddress))
            {
                subjectAlternativeNames.AddIpAddress(hostnameAddress);
            }
            else
            {
                subjectAlternativeNames.AddDnsName(options.Hostname);
            }

            subjectAlternativeNames.AddDnsName("localhost");
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());

            using var generated = request.Create(
                rootCertificate,
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(2),
                RandomNumberGenerator.GetBytes(16));
            using var certificateWithPrivateKey = generated.CopyWithPrivateKey(key);
            File.WriteAllBytes(certificatePath, certificateWithPrivateKey.Export(X509ContentType.Pfx));
            ProtectPrivateKey(certificatePath);
            EnsurePublicCertificate(options, certificateWithPrivateKey);
            return LoadPkcs12(certificatePath);
        }
    }

    /// <summary>
    /// Loads the local certificate authority used to sign h.localhost
    /// </summary>
    public static X509Certificate2 LoadRootCertificate(LocalNodeOptions options)
    {
        lock (CertificateLock)
        {
            Directory.CreateDirectory(GetCertificateDirectory(options));
            return LoadOrCreateRootCertificate(options);
        }
    }

    /// <summary>
    /// Gets the persistent h.localhost certificate path
    /// </summary>
    public static string GetCertificatePath(LocalNodeOptions options)
        => Path.Combine(GetCertificateDirectory(options), CertificateFileName);

    /// <summary>
    /// Gets the PEM-encoded h.localhost server certificate path
    /// </summary>
    public static string GetPublicCertificatePath(LocalNodeOptions options)
        => Path.Combine(GetCertificateDirectory(options), PublicCertificateFileName);

    /// <summary>
    /// Gets the private local certificate authority path
    /// </summary>
    public static string GetRootCertificatePath(LocalNodeOptions options)
        => Path.Combine(GetCertificateDirectory(options), RootCertificateFileName);

    /// <summary>
    /// Gets the PEM-encoded local certificate authority path
    /// </summary>
    public static string GetRootPublicCertificatePath(LocalNodeOptions options)
        => Path.Combine(GetCertificateDirectory(options), RootPublicCertificateFileName);

    /// <summary>
    /// Gets the directory shared by Local Nodes that use the same certificate authority
    /// </summary>
    public static string GetCertificateDirectory(LocalNodeOptions options)
        => string.IsNullOrWhiteSpace(options.CertificateDirectory)
            ? options.DataDirectory
            : options.CertificateDirectory;

    private static X509Certificate2 LoadOrCreateRootCertificate(LocalNodeOptions options)
    {
        var rootCertificatePath = GetRootCertificatePath(options);
        if (TryLoadCurrentRootCertificate(rootCertificatePath, out var currentCertificate))
        {
            EnsureRootPublicCertificate(options, currentCertificate);
            return currentCertificate;
        }

        PreserveInvalidCertificate(rootCertificatePath);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(RootSubject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature,
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
        File.WriteAllBytes(rootCertificatePath, generated.Export(X509ContentType.Pfx));
        ProtectPrivateKey(rootCertificatePath);
        EnsureRootPublicCertificate(options, generated);
        return LoadPkcs12(rootCertificatePath);
    }

    private static bool TryLoadCurrentRootCertificate(string certificatePath, out X509Certificate2 certificate)
    {
        certificate = null!;
        if (!File.Exists(certificatePath))
            return false;

        try
        {
            certificate = LoadPkcs12(certificatePath);
            var basicConstraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
            var reusable = certificate.HasPrivateKey
                && certificate.Subject == RootSubject
                && certificate.Issuer == certificate.Subject
                && basicConstraints?.CertificateAuthority == true
                && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow
                && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30);
            if (reusable)
                return true;

            certificate.Dispose();
            certificate = null!;
        }
        catch (CryptographicException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static bool TryLoadCurrentServerCertificate(
        string certificatePath,
        string hostname,
        X509Certificate2 rootCertificate,
        out X509Certificate2 certificate)
    {
        certificate = null!;
        if (!File.Exists(certificatePath))
            return false;

        try
        {
            certificate = LoadPkcs12(certificatePath);
            var reusable = certificate.HasPrivateKey
                && certificate.Issuer == rootCertificate.Subject
                && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow
                && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7)
                && MatchesEndpoint(certificate, hostname)
                && IsIssuedBy(certificate, rootCertificate);
            if (reusable)
                return true;

            certificate.Dispose();
            certificate = null!;
        }
        catch (CryptographicException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    /// <summary>
    /// Validates that the certificate covers the endpoint address, so a
    /// certificate issued for the previous endpoint is regenerated instead of
    /// failing TLS hostname verification inside the game
    /// </summary>
    private static bool MatchesEndpoint(X509Certificate2 certificate, string hostname)
    {
        if (IPAddress.TryParse(hostname, out var address))
        {
            return certificate.Extensions
                .OfType<X509SubjectAlternativeNameExtension>()
                .Any(extension => extension.EnumerateIPAddresses().Any(candidate => candidate.Equals(address)));
        }

        return string.Equals(
            certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false),
            hostname,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIssuedBy(X509Certificate2 certificate, X509Certificate2 rootCertificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return chain.Build(certificate);
    }

    private static X509Certificate2 LoadPkcs12(string certificatePath)
        => X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password: null, KeyStorageFlags);

    private static void EnsurePublicCertificate(LocalNodeOptions options, X509Certificate2 certificate)
        => WritePublicCertificate(GetPublicCertificatePath(options), certificate);

    private static void EnsureRootPublicCertificate(LocalNodeOptions options, X509Certificate2 certificate)
        => WritePublicCertificate(GetRootPublicCertificatePath(options), certificate);

    private static void WritePublicCertificate(string publicCertificatePath, X509Certificate2 certificate)
    {
        File.WriteAllText(publicCertificatePath, certificate.ExportCertificatePem());
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(publicCertificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void PreserveInvalidCertificate(string certificatePath)
    {
        if (!File.Exists(certificatePath))
            return;

        var invalidPath = certificatePath + $".invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        File.Move(certificatePath, invalidPath, overwrite: true);
        ProtectPrivateKey(invalidPath);
    }

    private static void ProtectPrivateKey(string certificatePath)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(certificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
