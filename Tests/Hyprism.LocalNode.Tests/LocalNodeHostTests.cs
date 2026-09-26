// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Hyprism.Core;
using Hyprism.Core.Game.Authentication;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Infrastructure;
using Hyprism.LocalNode;

namespace Hyprism.LocalNode.Tests;

public sealed class LocalNodeHostTests
{
    [Fact]
    public void Factory_CreatesNodesWithSeparateEndpointsAndStateDirectories()
    {
        var appDirectory = Path.Combine(Path.GetTempPath(), "HyprismLocalNodeFactoryTests_" + Guid.NewGuid());
        try
        {
            var logSession = new LogSessionPaths(
                appDirectory,
                new DateTimeOffset(2026, 8, 15, 20, 31, 42, 137, TimeSpan.FromHours(3)));
            var factory = new LocalNodeServiceFactory(
                new AppPathConfiguration(appDirectory),
                logSession);
            using var first = Assert.IsType<LocalNodeHost>(factory.Create());
            using var second = Assert.IsType<LocalNodeHost>(factory.Create());

            Assert.True(first.Options.ConfigureSystemTrust);
            Assert.NotEqual(first.EndpointDomain, second.EndpointDomain);
            Assert.NotEqual(first.Issuer, second.Issuer);
            Assert.Equal(
                logSession.GetLocalNodeLogPath(first.Options.Port),
                first.Options.LogFilePath);
            Assert.Equal(
                logSession.GetLocalNodeRequestJournalPath(first.Options.Port),
                first.Options.RequestJournalPath);
            Assert.Equal(
                logSession.GetLocalNodeLogPath(second.Options.Port),
                second.Options.LogFilePath);
        }
        finally
        {
            if (Directory.Exists(appDirectory))
                Directory.Delete(appDirectory, recursive: true);
        }
    }

    [Fact]
    public void CertificateStore_UsesOneCertificateForSeparateSessionDirectories()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "HyprismLocalNodeCertificateTests_" + Guid.NewGuid());
        try
        {
            var certificateDirectory = Path.Combine(rootDirectory, "Certificate");
            var firstOptions = new LocalNodeOptions(
                Path.Combine(rootDirectory, "Sessions", "first"),
                "127.0.0.1",
                8443,
                CertificateDirectory: certificateDirectory);
            var secondOptions = new LocalNodeOptions(
                Path.Combine(rootDirectory, "Sessions", "second"),
                "127.0.0.1",
                8444,
                CertificateDirectory: certificateDirectory);

            using var firstCertificate = LocalNodeCertificateStore.LoadOrCreate(firstOptions);
            using var secondCertificate = LocalNodeCertificateStore.LoadOrCreate(secondOptions);
            using var firstRootCertificate = LocalNodeCertificateStore.LoadRootCertificate(firstOptions);
            using var secondRootCertificate = LocalNodeCertificateStore.LoadRootCertificate(secondOptions);

            Assert.Equal(firstCertificate.Thumbprint, secondCertificate.Thumbprint);
            Assert.Equal(firstRootCertificate.Thumbprint, secondRootCertificate.Thumbprint);
            Assert.Equal(firstRootCertificate.Subject, firstCertificate.Issuer);
            Assert.Equal(
                LocalNodeCertificateStore.GetCertificatePath(firstOptions),
                LocalNodeCertificateStore.GetCertificatePath(secondOptions));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void TrustStore_FindsOnlyObsoleteHyprismRootCertificates()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "HyprismLocalNodeTrustTests_" + Guid.NewGuid());
        try
        {
            var currentOptions = new LocalNodeOptions(
                Path.Combine(rootDirectory, "Current"),
                "127.0.0.1",
                8443);
            var obsoleteOptions = new LocalNodeOptions(
                Path.Combine(rootDirectory, "Obsolete"),
                "127.0.0.1",
                8444);
            using var currentRoot = LocalNodeCertificateStore.LoadRootCertificate(currentOptions);
            using var obsoleteRoot = LocalNodeCertificateStore.LoadRootCertificate(obsoleteOptions);
            using var serverCertificate = LocalNodeCertificateStore.LoadOrCreate(obsoleteOptions);
            var certificates = new X509Certificate2Collection();
            certificates.AddRange(new X509Certificate2[] { currentRoot, obsoleteRoot, serverCertificate });

            var obsoleteCertificates = LocalNodeTrustStore.FindObsoleteHyprismRootCertificates(
                certificates,
                currentRoot);

            var obsoleteCertificate = Assert.Single(obsoleteCertificates.Cast<X509Certificate2>());
            Assert.Equal(obsoleteRoot.Thumbprint, obsoleteCertificate.Thumbprint);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AccountStore_PersistsProfilesAcrossIsolatedNodeSessions()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "HyprismLocalNodeAccountsTests_" + Guid.NewGuid());
        try
        {
            var accountDirectory = Path.Combine(rootDirectory, "LocalNode");
            var firstSession = new LocalAccountStore(accountDirectory);
            var secondSession = new LocalAccountStore(accountDirectory);
            using var firstSkin = JsonDocument.Parse("{\"haircut\":\"Quiff.Black\"}");
            using var secondSkin = JsonDocument.Parse("{\"haircut\":\"Morning.Brown\"}");

            await Task.WhenAll(
                firstSession.SaveSkinAsync(
                    "550e8400-e29b-41d4-a716-446655440000",
                    "FirstPlayer",
                    firstSkin.RootElement),
                secondSession.SaveSkinAsync(
                    "660e8400-e29b-41d4-a716-446655440000",
                    "SecondPlayer",
                    secondSkin.RootElement));

            var accountPath = Assert.Single(
                Directory.EnumerateFiles(accountDirectory),
                path => string.Equals(Path.GetFileName(path), "Accounts.json", StringComparison.Ordinal));
            using var accountDocument = JsonDocument.Parse(await File.ReadAllTextAsync(accountPath));
            Assert.All(
                accountDocument.RootElement.EnumerateObject(),
                property => Assert.True(char.IsUpper(property.Name[0]), property.Name));

            var restoredStore = new LocalAccountStore(accountDirectory);
            var firstProfile = await restoredStore.FindByUuidAsync("550e8400-e29b-41d4-a716-446655440000");
            var secondProfile = await restoredStore.FindByUuidAsync("660e8400-e29b-41d4-a716-446655440000");

            Assert.Equal("{\"haircut\":\"Quiff.Black\"}", firstProfile!.SkinJson);
            Assert.Equal("{\"haircut\":\"Morning.Brown\"}", secondProfile!.SkinJson);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Options_Parse_PreservesSharedAccountDirectory()
    {
        var accountDirectory = Path.Combine(Path.GetTempPath(), "HyprismSharedAccounts");
        var logFilePath = Path.Combine(Path.GetTempPath(), "HyprismLogs", "local-node-8443.log");
        var requestJournalPath = Path.Combine(
            Path.GetTempPath(),
            "HyprismLogs",
            "local-node-requests-8443.ndjson");

        var options = LocalNodeOptions.Parse(
        [
            "--data-directory", Path.Combine(Path.GetTempPath(), "HyprismSession"),
            "--account-data-directory", accountDirectory,
            "--log-file", logFilePath,
            "--request-journal", requestJournalPath
        ]);

        Assert.Equal(Path.GetFullPath(accountDirectory), options.AccountDataDirectory);
        Assert.Equal(Path.GetFullPath(logFilePath), options.LogFilePath);
        Assert.Equal(Path.GetFullPath(requestJournalPath), options.RequestJournalPath);
    }

    [Fact]
    public async Task Host_ExecutesAutonomousSessionAndAccountFlow()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "HyprismLocalNodeTests_" + Guid.NewGuid());
        var centralLogDirectory = Path.Combine(dataDirectory, "Logs", "test-session");
        var nodeLogPath = Path.Combine(centralLogDirectory, "local-node-8443.log");
        var requestJournalPath = Path.Combine(centralLogDirectory, "local-node-requests-8443.ndjson");
        var options = new LocalNodeOptions(
            dataDirectory,
            "127.0.0.1",
            GetAvailablePort(),
            LogFilePath: nodeLogPath,
            RequestJournalPath: requestJournalPath)
        {
            ConfigureSystemTrust = false
        };
        const string playerUuid = "550e8400-e29b-41d4-a716-446655440000";
        using (var initialSkin = JsonDocument.Parse(
                   "{\"bodyCharacteristic\":\"Default.01\",\"haircut\":\"MagicalPigtails.Blond\"}"))
        {
            await new LocalAccountStore(dataDirectory).SaveSkinAsync(
                playerUuid,
                "LocalPlayer",
                initialSkin.RootElement);
        }
        await using var host = new LocalNodeHost(options);

        try
        {
            await host.EnsureReadyAsync();
            var session = await host.CreateSessionAsync(
                playerUuid,
                "LocalPlayer");
            AssertEmbeddedKey(session.IdentityToken, expectPrivateKey: true);
            AssertEmbeddedKey(session.SessionToken, expectPrivateKey: false);
            AssertSkinClaim(session.IdentityToken, "MagicalPigtails.Blond");

            using var client = CreatePinnedClient(options);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                session.SessionToken);

            using var jwksResponse = await client.GetAsync("/.well-known/jwks.json");
            jwksResponse.EnsureSuccessStatusCode();
            using var jwks = JsonDocument.Parse(await jwksResponse.Content.ReadAsStringAsync());
            var publicKey = Assert.Single(jwks.RootElement.GetProperty("keys").EnumerateArray());
            Assert.False(publicKey.TryGetProperty("d", out _));

            using var profileResponse = await client.GetAsync("/my-account/game-profile");
            profileResponse.EnsureSuccessStatusCode();
            using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync());
            Assert.Equal("LocalPlayer", profile.RootElement.GetProperty("username").GetString());

            using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/game-session/refresh")
            {
                Content = null
            };
            using var refreshResponse = await client.SendAsync(refreshRequest);
            refreshResponse.EnsureSuccessStatusCode();
            using var refreshedSession = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
            Assert.NotEqual(
                session.IdentityToken,
                refreshedSession.RootElement.GetProperty("identityToken").GetString());
            Assert.NotEqual(
                session.SessionToken,
                refreshedSession.RootElement.GetProperty("sessionToken").GetString());
            AssertSkinClaim(
                refreshedSession.RootElement.GetProperty("identityToken").GetString()!,
                "MagicalPigtails.Blond");

            using var updateSkinResponse = await client.PostAsJsonAsync("/my-account/skin", new
            {
                bodyCharacteristic = "Default.01",
                haircut = "Quiff.Black",
                overtop = "OnePiece_SchoolDress.Black"
            });
            updateSkinResponse.EnsureSuccessStatusCode();

            var serverIdentity = new OmniAuthSessionIssuer(
                Guid.NewGuid().ToString(),
                "SingleplayerServer",
                options.Issuer).CreateSession("hytale:server");
            using var grantResponse = await client.PostAsJsonAsync("/game-session/authorize", new
            {
                identityToken = serverIdentity.IdentityToken,
                audience = "local-test-server",
                scope = new[] { "hytale:client", "hytale:server" }
            });
            grantResponse.EnsureSuccessStatusCode();
            using var grantJson = JsonDocument.Parse(await grantResponse.Content.ReadAsStringAsync());
            var grant = grantJson.RootElement.GetProperty("authorizationGrant").GetString();
            Assert.False(string.IsNullOrWhiteSpace(grant));
            AssertEmbeddedKey(grant!, expectPrivateKey: false);
            AssertSkinClaim(grant!, "Quiff.Black");

            using var exchangeResponse = await client.PostAsJsonAsync("/server-join/auth-token", new
            {
                authorizationGrant = grant,
                x509Fingerprint = "test-fingerprint"
            });
            exchangeResponse.EnsureSuccessStatusCode();
            using var exchange = JsonDocument.Parse(await exchangeResponse.Content.ReadAsStringAsync());
            var accessToken = exchange.RootElement.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(accessToken));
            AssertEmbeddedKey(accessToken!, expectPrivateKey: false);
            using var accessClaims = ReadTokenPart(accessToken!, 1);
            Assert.Equal(
                "test-fingerprint",
                accessClaims.RootElement.GetProperty("cnf").GetProperty("x5t#S256").GetString());
            AssertSkinClaim(accessToken!, "Quiff.Black");

            using var createSkinResponse = await client.PostAsJsonAsync("/player-skins", new
            {
                name = "Local Avatar",
                skinData = "{\"haircut\":\"local\"}"
            });
            Assert.Equal(HttpStatusCode.Created, createSkinResponse.StatusCode);
            using var createdSkin = JsonDocument.Parse(await createSkinResponse.Content.ReadAsStringAsync());
            var skinId = createdSkin.RootElement.GetProperty("skinId").GetString();

            using var activateResponse = await client.PutAsJsonAsync("/player-skins/active", new { skinId });
            Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);
            using var skinsResponse = await client.GetAsync("/player-skins");
            skinsResponse.EnsureSuccessStatusCode();
            using var skins = JsonDocument.Parse(await skinsResponse.Content.ReadAsStringAsync());
            Assert.Equal(skinId, skins.RootElement.GetProperty("activeSkin").GetString());

            var accountJson = await File.ReadAllTextAsync(Path.Combine(dataDirectory, "Accounts.json"));
            Assert.Contains(
                Directory.EnumerateFiles(dataDirectory),
                path => string.Equals(Path.GetFileName(path), "Accounts.json", StringComparison.Ordinal));
            Assert.Contains("\\\"haircut\\\"", accountJson);
            Assert.DoesNotContain("\\u0022", accountJson, StringComparison.OrdinalIgnoreCase);

            var nodeLog = await File.ReadAllTextAsync(nodeLogPath);
            Assert.Contains("POST /game-session/refresh -> 200", nodeLog);
            Assert.Contains("POST /game-session/authorize -> 200", nodeLog);
            Assert.False(File.Exists(Path.Combine(dataDirectory, "local-node.log")));

            using var unknownResponse = await client.GetAsync("/unknown-test-route?ignored=value");
            Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
            var requestJournal = await File.ReadAllTextAsync(requestJournalPath);
            Assert.Contains("unknown-test-route", requestJournal);
            Assert.Contains("ignored", requestJournal);
            Assert.DoesNotContain("value", requestJournal);
            Assert.False(File.Exists(Path.Combine(dataDirectory, "unimplemented-requests.ndjson")));

            var startInfo = new ProcessStartInfo();
            host.ApplyClientTrust(startInfo);
            Assert.Contains("javax.net.ssl.trustStore", startInfo.Environment["JAVA_TOOL_OPTIONS"]);
            Assert.Contains("javax.net.ssl.trustStore", startInfo.Environment["HYPRISM_LOCAL_NODE_JAVA_OPTIONS"]);
            if (!OperatingSystem.IsWindows())
            {
                var bundlePath = startInfo.Environment["SSL_CERT_FILE"];
                Assert.True(File.Exists(bundlePath));
                var bundle = await File.ReadAllTextAsync(bundlePath);
                var localRootCertificate = await File.ReadAllTextAsync(
                    LocalNodeCertificateStore.GetRootPublicCertificatePath(options));
                Assert.EndsWith(localRootCertificate, bundle);
                if (OperatingSystem.IsMacOS() && File.Exists("/etc/ssl/cert.pem"))
                    Assert.True(bundle.Length > localRootCertificate.Length);
            }
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Cosmetics_ReturnsInstalledAssetIdsInUpstreamShape()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "HyprismLocalNodeCosmeticsTests_" + Guid.NewGuid());
        var dataDirectory = Path.Combine(testDirectory, "LocalNode");
        var gameDirectory = Path.Combine(testDirectory, "Game");
        Directory.CreateDirectory(gameDirectory);
        CreateAssetsArchive(Path.Combine(gameDirectory, "Assets.zip"));

        var options = new LocalNodeOptions(dataDirectory, "127.0.0.1", GetAvailablePort())
        {
            ConfigureSystemTrust = false
        };
        await using var host = new LocalNodeHost(options);

        try
        {
            await host.EnsureReadyAsync(gameDirectory);
            var session = await host.CreateSessionAsync(
                "550e8400-e29b-41d4-a716-446655440000",
                "CosmeticPlayer");
            using var client = CreatePinnedClient(options);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                session.IdentityToken);

            using var response = await client.GetAsync("/my-account/cosmetics");
            response.EnsureSuccessStatusCode();
            using var cosmetics = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(JsonValueKind.Object, cosmetics.RootElement.ValueKind);
            Assert.False(cosmetics.RootElement.TryGetProperty("entitlements", out _));
            Assert.Equal(
                ["Morning", "Quiff"],
                cosmetics.RootElement.GetProperty("haircut")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray());
            Assert.Equal(
                "Cape_Royal_Emissary",
                Assert.Single(cosmetics.RootElement.GetProperty("cape").EnumerateArray()).GetString());
            Assert.Empty(cosmetics.RootElement.GetProperty("overtop").EnumerateArray());
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Host_TransfersLifetimeToGameAndNodeStopsAfterGameExit()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "HyprismLocalNodeLifetimeTests_" + Guid.NewGuid());
        var options = new LocalNodeOptions(testDirectory, "127.0.0.1", GetAvailablePort())
        {
            ConfigureSystemTrust = false
        };
        var host = new LocalNodeHost(options);
        Process? gameProcess = null;

        try
        {
            await host.EnsureReadyAsync();
            using var client = CreatePinnedClient(options);
            using var healthResponse = await client.GetAsync("/health");
            healthResponse.EnsureSuccessStatusCode();
            using var health = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
            Assert.NotEqual(Environment.ProcessId, health.RootElement.GetProperty("processId").GetInt32());

            gameProcess = StartIdleProcess();
            await host.AttachGameProcessAsync(gameProcess.Id);
            await host.DisposeAsync();

            using var attachedHealth = await client.GetAsync("/health");
            attachedHealth.EnsureSuccessStatusCode();
            using var attached = JsonDocument.Parse(await attachedHealth.Content.ReadAsStringAsync());
            Assert.Equal(
                gameProcess.Id,
                attached.RootElement.GetProperty("gameProcessId").GetInt32());

            gameProcess.Kill(entireProcessTree: true);
            await gameProcess.WaitForExitAsync();
            await WaitForNodeExitAsync(client);

            var localNodeLog = await File.ReadAllTextAsync(Path.Combine(testDirectory, "local-node.log"));
            Assert.Contains($"Lifetime attached to game process {gameProcess.Id}", localNodeLog);
            Assert.Contains($"Game process {gameProcess.Id} exited", localNodeLog);
            Assert.DoesNotContain("Microsoft.AspNetCore", localNodeLog);
        }
        finally
        {
            if (gameProcess is { HasExited: false })
                gameProcess.Kill(entireProcessTree: true);
            gameProcess?.Dispose();
            await host.DisposeAsync();
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorizationGrant_RejectsTokenWithModifiedPayload()
    {
        var sessions = new LocalSessionRegistry("https://h.localhost:8443");
        var session = sessions.Renew("uuid", "Player");
        var parts = session.IdentityToken.Split('.');
        using var claims = ReadTokenPart(session.IdentityToken, 1);
        var modifiedClaims = claims.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object)property.Value.Clone());
        modifiedClaims["username"] = "Impostor";
        parts[1] = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(modifiedClaims));

        Assert.Null(sessions.CreateAuthorizationGrant(
            string.Join('.', parts),
            "server",
            "hytale:client"));
    }

    private static HttpClient CreatePinnedClient(LocalNodeOptions options)
    {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            LocalNodeCertificateStore.GetCertificatePath(options),
            password: null,
            X509KeyStorageFlags.Exportable);
        var expectedCertificate = certificate.RawData;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented is not null && presented.RawData.AsSpan().SequenceEqual(expectedCertificate)
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{options.Port}"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static void AssertSkinClaim(string token, string expectedHaircut)
    {
        using var claims = ReadTokenPart(token, 1);
        var serializedSkin = claims.RootElement.GetProperty("skin").GetString();
        Assert.False(string.IsNullOrWhiteSpace(serializedSkin));
        using var skin = JsonDocument.Parse(serializedSkin!);
        Assert.Equal(expectedHaircut, skin.RootElement.GetProperty("haircut").GetString());
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Process StartIdleProcess()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/d /c ping 127.0.0.1 -n 31 > nul")
            : new ProcessStartInfo("/bin/sh", "-c \"sleep 30\"");
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Could not start the lifetime test process");
    }

    private static async Task WaitForNodeExitAsync(HttpClient client)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                using var response = await client.GetAsync("/health");
            }
            catch (HttpRequestException)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The Local Node did not stop after the game process exited");
    }

    private static void CreateAssetsArchive(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteCatalogEntry(
            archive,
            "Haircuts.json",
            """[{"Id":"Morning"},{"Id":"Quiff"},{"Name":"Missing ID"}]""");
        WriteCatalogEntry(
            archive,
            "Capes.json",
            """[{"Id":"Cape_Royal_Emissary"}]""");
    }

    private static void WriteCatalogEntry(ZipArchive archive, string fileName, string json)
    {
        var entry = archive.CreateEntry($"Cosmetics/CharacterCreator/{fileName}");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(json);
    }

    private static void AssertEmbeddedKey(string token, bool expectPrivateKey)
    {
        using var header = ReadTokenPart(token, 0);
        var jwk = header.RootElement.GetProperty("jwk");
        Assert.Equal(expectPrivateKey, jwk.TryGetProperty("d", out _));
    }

    private static JsonDocument ReadTokenPart(string token, int index)
        => JsonDocument.Parse(Base64UrlDecode(token.Split('.')[index]));

    private static string Base64UrlEncode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
