// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Hyprism.Core.Game.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;

namespace Hyprism.LocalNode;

/// <summary>
/// Builds the loopback HTTP compatibility surface used by the Hytale client
/// </summary>
public static class LocalNodeApplication
{
    private static readonly Dictionary<string, object> DefaultPresenceSettings = new()
    {
        ["allowFriendRequests"] = true,
        ["allowInvites"] = 1,
        ["allowJoin"] = true,
        ["showActivity"] = 1,
        ["showLocation"] = 1,
        ["showOnline"] = 1
    };

    /// <summary>
    /// Creates a configured web application without starting it
    /// </summary>
    public static WebApplication Build(
        LocalNodeOptions options,
        X509Certificate2 certificate,
        LocalSessionRegistry? sessions = null,
        LocalAccountStore? accounts = null,
        LocalCosmeticsCatalog? cosmetics = null,
        LocalNodeLog? log = null,
        LocalNodeProcessLifetime? processLifetime = null)
    {
        sessions ??= new LocalSessionRegistry(options.Issuer);
        accounts ??= new LocalAccountStore(options.AccountDataDirectory ?? options.DataDirectory);
        cosmetics ??= new LocalCosmeticsCatalog(options.AssetsPath, log);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(LocalNodeApplication).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Services.Configure<JsonOptions>(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        });
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
            server.ListenLocalhost(options.Port, listener => listener.UseHttps(certificate));
        });

        var app = builder.Build();
        var journal = new RequestJournal(options.DataDirectory, options.RequestJournalPath);

        app.Use(async (context, next) =>
        {
            var remoteAddress = context.Connection.RemoteIpAddress;
            if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var host = context.Request.Host.Host;
            if (!string.Equals(host, options.Hostname, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                && host != "127.0.0.1"
                && host != "::1")
            {
                context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            await next(context);
        });

        app.Use(async (context, next) =>
        {
            var stopwatch = Stopwatch.StartNew();
            Exception? failure = null;
            try
            {
                await next(context);
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                if (log is not null && ShouldLogRequest(context.Request.Path, context.Response.StatusCode))
                {
                    var message = $"{context.Request.Method} {context.Request.Path} -> "
                                  + $"{context.Response.StatusCode} ({stopwatch.ElapsedMilliseconds}ms)";
                    if (failure is not null)
                        log.Error($"{message}: {failure.GetType().Name}: {failure.Message}");
                    else if (context.Response.StatusCode >= 400)
                        log.Warning(message);
                    else
                        log.Info(message);
                }
            }
        });

        MapHealthAndSessionRoutes(app, options, sessions, accounts, processLifetime);
        MapAccountRoutes(app, sessions, accounts, cosmetics);
        MapCompatibilityRoutes(app, sessions, accounts);

        app.MapFallback(async context =>
        {
            journal.Append(context.Request.Method, context.Request.Path, context.Request.Query.Keys);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "endpoint_not_implemented",
                message = "The Local Node recorded this request path for compatibility analysis"
            });
        });

        return app;
    }

    private static void MapHealthAndSessionRoutes(
        WebApplication app,
        LocalNodeOptions options,
        LocalSessionRegistry sessions,
        LocalAccountStore accounts,
        LocalNodeProcessLifetime? processLifetime)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ready",
            server = "hyprism-local-node",
            issuer = options.Issuer,
            protocol = "https",
            hostname = options.Hostname,
            port = options.Port,
            processId = Environment.ProcessId,
            gameProcessId = processLifetime?.GameProcessId
        }));

        if (processLifetime is not null && !string.IsNullOrWhiteSpace(options.ControlSecret))
        {
            app.MapPost("/v1/lifecycle/attach", (HttpContext context, JsonElement body) =>
            {
                if (!IsControlAuthorized(context.Request, options.ControlSecret))
                    return Results.Unauthorized();
                if (!body.TryGetProperty("gameProcessId", out var processIdElement)
                    || !processIdElement.TryGetInt32(out var processId))
                {
                    return Results.BadRequest(new { error = "gameProcessId is required" });
                }

                return processLifetime.TryAttachGameProcess(processId, app.Lifetime, out var error)
                    ? Results.Ok(new { attached = true, gameProcessId = processId })
                    : Results.Conflict(new { error });
            });

            app.MapPost("/v1/lifecycle/stop", (HttpContext context) =>
            {
                if (!IsControlAuthorized(context.Request, options.ControlSecret))
                    return Results.Unauthorized();
                processLifetime.Stop("Shutdown requested by launcher", app.Lifetime);
                return Results.Accepted();
            });
        }

        app.MapGet("/.well-known/jwks.json", () => Results.Ok(new { keys = sessions.GetPublicKeys() }));
        app.MapGet("/jwks.json", () => Results.Ok(new { keys = sessions.GetPublicKeys() }));

        app.MapPost("/v1/sessions", async (JsonElement body, CancellationToken cancellationToken) =>
        {
            var uuid = GetString(body, "playerUuid", "uuid");
            var name = GetString(body, "playerName", "username", "name");
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                return MissingIdentity();

            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return SessionResult(sessions.Renew(uuid, name, skin: profile.SkinJson));
        });

        async Task<(JsonElement Body, string? Uuid, string? Name)> ReadSessionRequest(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var body = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                ? await context.Request.ReadFromJsonAsync<JsonElement>(cancellationToken)
                : default;
            var uuid = GetString(body, "uuid", "playerUuid", "profileId");
            var name = GetString(body, "username", "name", "playerName");
            if ((string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                && TryGetBearerIdentity(context.Request, sessions, out var bearerUuid, out var bearerName))
            {
                if (string.IsNullOrWhiteSpace(uuid))
                    uuid = bearerUuid;
                if (string.IsNullOrWhiteSpace(name))
                    name = bearerName;
            }
            return (body, uuid, name);
        }

        async Task<IResult> CreateGameSession(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var (body, uuid, name) = await ReadSessionRequest(context, cancellationToken);
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                return MissingIdentity();

            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return SessionResult(sessions.Renew(uuid, name, skin: profile.SkinJson));
        }

        async Task<IResult> CreateChildSession(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var (body, uuid, name) = await ReadSessionRequest(context, cancellationToken);
            var profile = !string.IsNullOrWhiteSpace(uuid) && !string.IsNullOrWhiteSpace(name)
                ? await accounts.GetOrCreateAsync(uuid, name, cancellationToken)
                : null;
            var proofToken = GetBearerToken(context.Request)
                             ?? GetString(body, "sessionToken", "session_token", "identityToken", "identity_token");
            var session = !string.IsNullOrWhiteSpace(proofToken)
                ? sessions.RenewByToken(
                    proofToken,
                    GetScope(body) ?? "hytale:server",
                    skin: profile?.SkinJson)
                : null;

            if (session is null)
            {
                if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                    return Results.Unauthorized();
                session = sessions.Renew(
                    uuid,
                    name,
                    GetScope(body) ?? "hytale:server",
                    skin: profile?.SkinJson);
            }

            return SessionResult(session);
        }

        async Task<IResult> RefreshGameSession(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            var (body, uuid, name) = await ReadSessionRequest(context, cancellationToken);
            var profile = !string.IsNullOrWhiteSpace(uuid) && !string.IsNullOrWhiteSpace(name)
                ? await accounts.GetOrCreateAsync(uuid, name, cancellationToken)
                : null;
            var proofToken = GetBearerToken(context.Request)
                             ?? GetString(body, "sessionToken", "session_token", "identityToken", "identity_token");
            if (string.IsNullOrWhiteSpace(proofToken))
                return Results.Unauthorized();

            var session = sessions.RenewByToken(
                proofToken,
                GetScope(body) ?? "hytale:server hytale:client",
                skin: profile?.SkinJson);
            return session is null ? Results.Unauthorized() : SessionResult(session);
        }

        app.MapPost("/game-session", CreateGameSession);
        app.MapPost("/game-session/new", CreateGameSession);
        app.MapPost("/game-session/child", CreateChildSession);
        app.MapPost("/game-session/refresh", RefreshGameSession);

        app.MapDelete("/game-session", (HttpContext context) =>
        {
            var bearer = GetBearerToken(context.Request);
            if (!string.IsNullOrWhiteSpace(bearer))
                sessions.RemoveByToken(bearer);
            return Results.NoContent();
        });

        async Task<IResult> CreateAuthorizationGrant(
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken)
        {
            var identityToken = GetString(body, "identityToken", "identity_token", "token");
            var audience = GetString(body, "aud", "audience", "serverAudience", "server_id");
            var scope = GetScope(body);
            if (string.IsNullOrWhiteSpace(audience))
                return Results.BadRequest(new { error = "audience is required" });

            var bearer = GetBearerToken(context.Request);
            LocalProfileData? profile = null;
            if (TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            var grant = !string.IsNullOrWhiteSpace(bearer)
                ? sessions.CreateAuthorizationGrant(bearer, audience, scope, profile?.SkinJson)
                : null;
            if (grant is null && !string.IsNullOrWhiteSpace(identityToken))
                grant = sessions.CreateAuthorizationGrant(identityToken, audience, scope);
            return grant is null
                ? Results.Unauthorized()
                : Results.Ok(new
                {
                    authorizationGrant = grant,
                    expiresAt = DateTimeOffset.UtcNow.AddHours(10)
                });
        }

        app.MapPost("/game-session/authorize", CreateAuthorizationGrant);
        app.MapPost("/server-join/auth-grant", CreateAuthorizationGrant);

        IResult ExchangeGrant(JsonElement body)
        {
            var grant = GetString(body, "authorizationGrant", "authorization_grant", "grant");
            if (string.IsNullOrWhiteSpace(grant))
                return Results.BadRequest(new { error = "authorizationGrant is required" });

            var fingerprint = GetString(body, "x509Fingerprint", "certFingerprint", "fingerprint");
            var token = sessions.ExchangeAuthorizationGrant(
                grant,
                fingerprint,
                GetScope(body),
                out var scope,
                out var refreshToken);
            if (token is null)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                accessToken = token,
                tokenType = "Bearer",
                expiresIn = 36000,
                expiresAt = DateTimeOffset.UtcNow.AddHours(10),
                scope,
                refreshToken
            });
        }

        app.MapPost("/server-join/auth-token", ExchangeGrant);
        app.MapPost("/game-session/exchange", ExchangeGrant);

        IResult Validate(HttpContext context)
        {
            var bearer = GetBearerToken(context.Request);
            return bearer is not null && sessions.TryValidate(bearer, out _)
                ? Results.Ok(new { valid = true, success = true })
                : Results.Unauthorized();
        }

        app.MapMethods("/validate", ["GET", "POST"], Validate);
        app.MapMethods("/verify", ["GET", "POST"], Validate);

        app.MapPost("/server/auto-auth", (JsonElement body) =>
        {
            var serverId = GetString(body, "serverUuid", "serverId", "server_id") ?? Guid.NewGuid().ToString();
            var serverName = GetString(body, "serverName", "server_name") ?? $"Server-{serverId[..Math.Min(8, serverId.Length)]}";
            var session = sessions.Renew(serverId, serverName, "hytale:server", ["game.base", "server.host"]);
            return Results.Ok(new
            {
                identityToken = session.IdentityToken,
                sessionToken = session.SessionToken,
                expiresIn = 36000,
                expiresAt = session.ExpiresAt,
                tokenType = "Bearer",
                serverId,
                serverUuid = serverId,
                serverName
            });
        });

        app.MapGet("/server/game-profiles", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new[] { new { uuid, username = name, isDefault = true } });
        });
        app.MapGet("/game-profiles", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new[] { new { uuid, username = name, isDefault = true } });
        });

        app.MapGet("/api/check-identity", (HttpContext context) =>
        {
            var uuid = context.Request.Query["uuid"].ToString();
            var username = context.Request.Query["username"].ToString();
            return string.IsNullOrWhiteSpace(uuid) && string.IsNullOrWhiteSpace(username)
                ? Results.BadRequest(new { error = "uuid or username required" })
                : Results.Ok(new { allowed = true });
        });
    }

    private static void MapAccountRoutes(
        WebApplication app,
        LocalSessionRegistry sessions,
        LocalAccountStore accounts,
        LocalCosmeticsCatalog cosmetics)
    {
        async Task<IResult> GameProfile(HttpContext context, CancellationToken cancellationToken)
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return Results.Ok(new
            {
                uuid = profile.Uuid,
                username = profile.Username,
                entitlements = new[] { "game.base" },
                createdAt = "2026-01-01T00:00:00Z",
                nextNameChangeAt = DateTimeOffset.UtcNow.AddDays(30),
                skin = profile.SkinJson,
                password_protected = false
            });
        }

        app.MapMethods("/my-account/game-profile", ["GET", "POST"], GameProfile);

        app.MapGet("/my-account/get-profiles", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new { owner = uuid, profiles = new[] { new { uuid, username = name } } });
        });

        app.MapGet("/my-account/get-launcher-data", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            return Results.Ok(new
            {
                eulaAcceptedAt = "2026-01-01T00:00:00Z",
                owner = uuid,
                patchlines = new
                {
                    preRelease = new { buildVersion = "local", newest = 1 },
                    release = new { buildVersion = "local", newest = 1 }
                },
                profiles = new[] { new { uuid, username = name, entitlements = new[] { "game.base" } } }
            });
        });

        app.MapGet("/profile/uuid/{uuid}", async (string uuid, CancellationToken cancellationToken) =>
        {
            var profile = await accounts.FindByUuidAsync(uuid, cancellationToken);
            return profile is null
                ? Results.NotFound(new { error = "Profile not found" })
                : Results.Ok(new { uuid = profile.Uuid, username = profile.Username, skin = profile.SkinJson });
        });

        app.MapGet("/profile/username/{username}", async (string username, CancellationToken cancellationToken) =>
        {
            var profile = await accounts.FindByUsernameAsync(username, cancellationToken);
            return profile is null
                ? Results.NotFound(new { error = "Profile not found" })
                : Results.Ok(new { uuid = profile.Uuid, username = profile.Username });
        });

        app.MapPost("/my-account/skin", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            await accounts.SaveSkinAsync(uuid, name, body, cancellationToken);
            return Results.NoContent();
        });

        app.MapPost("/account-data/skin/{uuid}", async (
            HttpContext context,
            string uuid,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var tokenUuid, out var name)
                || !string.Equals(uuid, tokenUuid, StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();
            await accounts.SaveSkinAsync(uuid, name, body, cancellationToken);
            return Results.NoContent();
        });

        app.MapGet("/my-account/cosmetics", (HttpContext context) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out _, out _))
                return Results.Unauthorized();
            return Results.Ok(cosmetics.GetUnlockedCosmetics());
        });

        app.MapGet("/player-skins", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return Results.Ok(new
            {
                activeSkin = profile.ActiveSkinId,
                maxSkins = 10,
                skins = profile.PlayerSkins.Select(skin => new
                {
                    id = skin.Id,
                    name = skin.Name,
                    skinData = skin.SkinData,
                    createdAt = skin.CreatedAt
                })
            });
        });

        app.MapPost("/player-skins", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var username))
                return Results.Unauthorized();
            var skinData = GetString(body, "skinData");
            if (skinData is null)
                return Results.BadRequest(new { error = "skinData is required" });
            var skinId = await accounts.CreatePlayerSkinAsync(
                uuid,
                username,
                GetString(body, "name") ?? "Avatar",
                skinData,
                cancellationToken);
            return Results.Created($"/player-skins/{skinId}", new { skinId });
        });

        app.MapPut("/player-skins/active", async (
            HttpContext context,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            var skinId = GetString(body, "skinId");
            if (skinId is null)
                return Results.BadRequest(new { error = "skinId is required" });
            return await accounts.SetActivePlayerSkinAsync(uuid, skinId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "Skin not found" });
        });

        app.MapPut("/player-skins/{skinId}", async (
            HttpContext context,
            string skinId,
            JsonElement body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return await accounts.UpdatePlayerSkinAsync(
                uuid,
                skinId,
                GetString(body, "name"),
                GetString(body, "skinData"),
                cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "Skin not found" });
        });

        app.MapDelete("/player-skins/{skinId}", async (
            HttpContext context,
            string skinId,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            return await accounts.DeletePlayerSkinAsync(uuid, skinId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new { error = "Skin not found" });
        });
    }

    private static void MapCompatibilityRoutes(
        WebApplication app,
        LocalSessionRegistry sessions,
        LocalAccountStore accounts)
    {
        object LocalConfig() => new
        {
            flags = new Dictionary<string, object>
            {
                ["enable_discord_integration"] = new { type = "boolean", value = false },
                ["enable_in_game_discord_link"] = new { type = "boolean", value = false },
                ["enable_new_server_discovery"] = new { type = "boolean", value = false },
                ["enable_news_tiles"] = new { type = "boolean", value = false },
                ["enable_social_layer"] = new { type = "boolean", value = true }
            },
            version = "hyprism-local-v1"
        };
        app.MapGet("/configs", () => Results.Ok(LocalConfig()));
        app.MapGet("/configs/{**path}", () => Results.Ok(LocalConfig()));
        app.MapGet("/v1/release/any/any/feature-flags/{hash}.json", (string hash) =>
            Results.Ok(LocalConfig()));
        app.MapGet("/liveconfig/manifest.json", () => Results.Ok(new
        {
            version = "hyprism-local-v1",
            patchline = "release",
            configs = new Dictionary<string, object>()
        }));
        app.MapGet("/news-tiles", () => Results.Ok(new { tiles = Array.Empty<object>() }));

        app.MapGet("/friends", () => Results.Ok(new { friends = Array.Empty<object>(), truncated = false }));
        app.MapGet("/friends/favorites", () => Results.Ok(new { favorites = Array.Empty<object>() }));
        app.MapGet("/presence/friends", () => Results.Ok(new { friends = Array.Empty<object>() }));
        app.MapGet("/friend-requests/outgoing", () => Results.Ok(new { requests = Array.Empty<object>(), truncated = false }));
        app.MapGet("/friend-requests/incoming", () => Results.Ok(new { requests = Array.Empty<object>(), truncated = false }));
        app.MapGet("/party/invites", () => Results.Ok(new { invites = Array.Empty<object>() }));
        app.MapGet("/party/invites/sent", () => Results.Ok(new { invites = Array.Empty<object>() }));
        app.MapGet("/party", () => Results.Text("not in a party", statusCode: StatusCodes.Status404NotFound));
        app.MapGet("/world-invites", () => Results.Ok(new { invites = Array.Empty<object>() }));
        app.MapGet("/world-invites/sent", () => Results.Ok(new { invites = Array.Empty<object>() }));
        app.MapGet("/blocks", () => Results.Ok(new { blocks = Array.Empty<object>(), truncated = false }));
        app.MapPost("/friend-requests/by-username", () =>
            Results.Text("profile not found", statusCode: StatusCodes.Status404NotFound));
        app.MapMethods("/me/interactions/{**path}",
            ["GET", "POST", "PUT", "PATCH", "DELETE"],
            () => Results.StatusCode(StatusCodes.Status501NotImplemented));

        app.MapGet("/presence/settings", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out var name))
                return Results.Unauthorized();
            var profile = await accounts.GetOrCreateAsync(uuid, name, cancellationToken);
            return Results.Ok(profile.PresenceSettings.Count == 0
                ? DefaultPresenceSettings
                : profile.PresenceSettings.ToDictionary(pair => pair.Key, pair => (object)pair.Value));
        });
        app.MapPut("/presence/settings", async (
            HttpContext context,
            Dictionary<string, JsonElement> body,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetBearerIdentity(context.Request, sessions, out var uuid, out _))
                return Results.Unauthorized();
            await accounts.SavePresenceSettingsAsync(uuid, body, cancellationToken);
            return Results.NoContent();
        });
        app.MapPost("/presence/heartbeat", () => Results.NoContent());
        app.MapPost("/telemetry", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/telemetry/{**path}", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/analytics", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/analytics/{**path}", () => Results.Ok(new { success = true, received = true }));
        app.MapPost("/bugs/create", () => Results.NoContent());
        app.MapPost("/feedback/create", () => Results.NoContent());
        app.MapGet("/servers/listings", () => Results.Ok(Array.Empty<object>()));
    }

    private static IResult SessionResult(OmniAuthSession session)
        => Results.Ok(new
        {
            identityToken = session.IdentityToken,
            sessionToken = session.SessionToken,
            expiresIn = 36000,
            expiresAt = session.ExpiresAt,
            tokenType = "Bearer"
        });

    private static IResult MissingIdentity()
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["uuid"] = ["A player UUID is required"],
            ["username"] = ["A player name is required"]
        });

    private static bool ShouldLogRequest(PathString path, int statusCode)
        => statusCode >= 400
           || path.StartsWithSegments("/game-session")
           || path.StartsWithSegments("/server-join")
           || path.StartsWithSegments("/v1/sessions")
           || path.StartsWithSegments("/v1/lifecycle");

    private static string? GetString(JsonElement body, params string[] names)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            if (body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private static string? GetScope(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return null;
        if (!body.TryGetProperty("scope", out var value) && !body.TryGetProperty("scopes", out value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => string.Join(' ', value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())),
            _ => null
        };
    }

    private static string? GetBearerToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : null;
    }

    private static bool IsControlAuthorized(HttpRequest request, string expectedSecret)
    {
        var suppliedSecret = request.Headers["X-Hyprism-Control"].ToString();
        if (suppliedSecret.Length != expectedSecret.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(suppliedSecret),
            Encoding.UTF8.GetBytes(expectedSecret));
    }

    private static bool TryGetBearerIdentity(
        HttpRequest request,
        LocalSessionRegistry sessions,
        out string uuid,
        out string name)
    {
        uuid = string.Empty;
        name = string.Empty;
        var bearer = GetBearerToken(request);
        if (bearer is null || !sessions.TryValidate(bearer, out var claims))
            return false;

        uuid = claims.GetProperty("sub").GetString() ?? string.Empty;
        if (claims.TryGetProperty("username", out var username))
            name = username.GetString() ?? string.Empty;
        if (name.Length == 0 && claims.TryGetProperty("name", out var displayName))
            name = displayName.GetString() ?? string.Empty;
        return uuid.Length > 0 && name.Length > 0;
    }
}
