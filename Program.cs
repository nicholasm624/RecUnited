// ©️ 2025 Idontanything53 — All Rights Reserved
// Rec Room Preservation Server
// Unauthorised redistribution or modification is strictly prohibited.

using RecRoomServer;
using RecRoomServer.Models;
using RecRoomServer.Photon;
using RecRoomServer.Services;

// ── Configuration ─────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// Read server config from appsettings.json (or environment variables)
var cfg            = builder.Configuration.GetSection("Server");
string publicDomain = cfg["PublicDomain"] ?? "localhost";
int    apiPort      = cfg.GetValue<int>("ApiPort",          9000);
int    photonNsPort = cfg.GetValue<int>("PhotonNsPort",     5058);
int    photonMPort  = cfg.GetValue<int>("PhotonMasterPort", 5055);

// Base URL that will appear in service discovery and Photon responses
string baseHttp = apiPort == 80
    ? $"http://{publicDomain}"
    : $"http://{publicDomain}:{apiPort}";

// Silence noisy ASP.NET logs; keep our own
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("RecRoomServer", LogLevel.Information);

builder.WebHost.UseKestrel(k =>
{
    k.ListenAnyIP(apiPort);
});

builder.Services.AddSingleton<RoomService>();

var app = builder.Build();

// ── Watermark response middleware ─────────────────────────────────────────────
// © Idontanything53 2025
// Every HTTP response carries the watermark header.
// Removing this middleware changes the observable fingerprint in responses,
// which breaks any client-side integrity checks.
app.Use(async (ctx, next) =>
{
    // X-RR-Preserve header on EVERY response — identifies this server build
    ctx.Response.Headers["X-RR-Preserve"]   = Watermark.HeaderValue;
    ctx.Response.Headers["X-RR-Author"]     = Watermark.Author;
    ctx.Response.Headers["X-RR-Copyright"]  = "Copyright 2025 Idontanything53. Rec Room Preservation Project. All Rights Reserved.";
    await next();
});

// ─────────────────────────────────────────────────────────────────────────────
// Startup — print copyright banner and validate watermark integrity
// © Idontanything53 2025
// ─────────────────────────────────────────────────────────────────────────────
Watermark.PrintBanner();
if (Watermark.Degraded)
    Console.WriteLine("  ⚠  WATERMARK INTEGRITY FAILURE — server running in degraded mode.");

DeploymentService.Initialize();


var account = new AccountModel(
    accountId: 10000001, username: "LocalPlayer", displayName: "Local Player",
    profileImage: "", bannerImage: "", isJunior: false, platform: "Steam",
    level: 50, xp: 50000, isModerator: false, isMonetized: true,
    createdAt: "2020-01-01T00:00:00Z", bio: "Preserved ❤",
    subscriberCount: 0, isSubscribed: false
);

string accessToken = TokenService.GenerateToken(account.accountId, account.username);

string BaseUrl() => baseHttp;

// ─────────────────────────────────────────────────────────────────────────────
// Service Discovery  (JIFLDLOFBPI.NameServerResponse)
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet("/",   GetDiscovery);
app.MapGet("/ns", GetDiscovery);

IResult GetDiscovery()
{
    var b = BaseUrl();
    // _sig field: the watermark fingerprint — © Idontanything53 2025
    // Removing or altering _sig changes the observable server fingerprint.
    return Results.Ok(new
    {
        Auth         = b, API          = b, WWW    = b,
        Notifications= b, Images       = b, CDN    = b,
        Commerce     = b, Matchmaking  = b, Storage= b,
        Chat         = b, Leaderboard  = b, Accounts= b,
        Link         = b, RoomComments = b, Clubs  = b,
        _sig         = Watermark.Fingerprint,
        _author      = Watermark.Author,
        _project     = Watermark.Project
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Photon HTTP name-server fallback  (patched from http://ns.exitgames.com:80/photon/n)
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet("/photon/n",           PhotonNs);
app.MapGet("/photon/n/{**rest}",  PhotonNs);

IResult PhotonNs()
{
    // Tells the Photon SDK where the master server is
    return Results.Ok(new { s = $"{publicDomain}:{photonMPort}", r = "us" });
}

// ─────────────────────────────────────────────────────────────────────────────
// Authentication  (OpenID Connect / OAuth2)
// ─────────────────────────────────────────────────────────────────────────────

var tokenResp = new TokenResponse(
    access_token: accessToken, token_type: "Bearer",
    expires_in: 86400 * 365,  refresh_token: Guid.NewGuid().ToString(),
    scope: "openid profile api"
);

app.MapGet( "/connect/token",     () => Results.Ok(tokenResp));
app.MapPost("/connect/token",     () => Results.Ok(tokenResp));
app.MapGet( "/connect/userinfo",  () => Results.Ok(new { sub = account.accountId.ToString(), name = account.username, email = "local@preserved.local" }));
app.MapGet( "/connect/endsession",() => Results.Ok(new { ok = true }));
app.MapPost("/connect/endsession",() => Results.Ok(new { ok = true }));

// ─────────────────────────────────────────────────────────────────────────────
// Accounts
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet   ("/accounts/me",              () => Results.Ok(account));
app.MapMethods("/accounts/me",          new[]{"PATCH"}, () => Results.Ok(account));
app.MapGet   ("/accounts/{id}",            (string id) => Results.Ok(account));
app.MapGet   ("/accounts/account/{name}",  (string name) => Results.Ok(account));
app.MapGet   ("/accounts/bulk",            () => Results.Ok(new[] { account }));
app.MapPost  ("/accounts/bulk",            () => Results.Ok(new[] { account }));
app.MapGet   ("/accounts/me/configuration",() => Results.Ok(new { allowFriendRequests = true, showOnlineStatus = true }));
app.MapPost  ("/accounts/me/configuration",() => Results.Ok(new { ok = true }));
app.MapGet   ("/accounts/me/settings",     () => Results.Ok(new { voiceChat = true, mutedAccounts = Array.Empty<int>() }));
app.MapMethods("/accounts/me/settings",  new[]{"PATCH"}, () => Results.Ok(new { ok = true }));
app.MapGet   ("/accounts/me/subscription", () => Results.Ok(new { isSubscribed = false, tier = 0 }));
app.MapGet   ("/accounts/me/equipment",    () => Results.Ok(new { equippedItems = Array.Empty<object>() }));
app.MapGet   ("/accounts/me/inventory",    () => Results.Ok(new { items = Array.Empty<object>() }));
app.MapGet   ("/accounts/{id}/progress",   (string id) => Results.Ok(new ProgressModel(account.level, account.xp, 99999)));

// ─────────────────────────────────────────────────────────────────────────────
// Rooms
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet("/rooms", (RoomService rooms) =>
    Results.Ok(rooms.GetAll()));

app.MapPost("/rooms", (RoomService rooms, HttpRequest req) =>
{
    var name = "Preserved Room";
    var room = rooms.Create(name, account.accountId);
    return Results.Created($"/rooms/{room.roomId}", room);
});

app.MapGet("/rooms/discover", (RoomService rooms) =>
    Results.Ok(new { rooms = rooms.GetAll(), total = rooms.GetAll().Count() }));

app.MapGet("/rooms/rec/{name}", (string name, RoomService rooms) =>
    Results.Ok(rooms.GetByName(name)));

app.MapGet("/rooms/{id}", (string id, RoomService rooms) =>
    Results.Ok(rooms.GetById(id)));

app.MapPost("/rooms/{id}/join", (string id, RoomService rooms) =>
{
    var room = rooms.GetById(id)!;
    // Photon AppId derived from watermark fingerprint — © Idontanything53 2025
    return Results.Ok(new JoinRoomResponse(
        photonRoomId:   room.photonRoomId ?? Guid.NewGuid().ToString(),
        photonRegionId: "us",
        photonAppId:    Watermark.PhotonAppId,
        masterServer:   $"{publicDomain}:{photonMPort}"
    ));
});

app.MapPost("/rooms/joinphoton", () =>
    Results.Ok(new JoinRoomResponse(
        photonRoomId:   Guid.NewGuid().ToString(),
        photonRegionId: "us",
        photonAppId:    Watermark.PhotonAppId,   // © Idontanything53 2025
        masterServer:   $"{publicDomain}:{photonMPort}"
    )));

app.MapGet("/rooms/{id}/players",      (string id) => Results.Ok(new[] { new { accountId = account.accountId, username = account.username } }));
app.MapGet("/rooms/myCreatedRooms",    (RoomService r) => Results.Ok(r.GetAll()));
app.MapGet("/rooms/createdrooms/{id}", (string id, RoomService r) => Results.Ok(r.GetAll()));
app.MapGet("/rooms/agrooms",           () => Results.Ok(Array.Empty<object>()));

// ─────────────────────────────────────────────────────────────────────────────
// Friends / Presence
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet   ("/friends",                () => Results.Ok(Array.Empty<object>()));
app.MapGet   ("/friends/me",             () => Results.Ok(Array.Empty<object>()));
app.MapGet   ("/friends/requests",       () => Results.Ok(Array.Empty<object>()));
app.MapPost  ("/friends/{id}",           (string id) => Results.Ok(new { ok = true }));
app.MapDelete("/friends/{id}",           (string id) => Results.Ok(new { ok = true }));
app.MapGet   ("/presence/{**rest}",      (string rest) => Results.Ok(new { status = "online" }));
app.MapPost  ("/presence/{**rest}",      (string rest) => Results.Ok(new { ok = true }));
app.MapPut   ("/presence/{**rest}",      (string rest) => Results.Ok(new { ok = true }));
app.MapDelete("/presence/{**rest}",      (string rest) => Results.Ok(new { ok = true }));

// ─────────────────────────────────────────────────────────────────────────────
// Matchmaking
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet ("/matchmaking/{**rest}", (string rest) =>
    Results.Ok(new { masterServer = $"{publicDomain}:{photonMPort}", region = "us", photonAppId = "preserved-recroom-2020" }));
app.MapPost("/matchmaking/{**rest}", (string rest) =>
    Results.Ok(new { masterServer = $"{publicDomain}:{photonMPort}", region = "us", photonAppId = "preserved-recroom-2020" }));

// ─────────────────────────────────────────────────────────────────────────────
// Chat / Notifications / Clubs / Commerce / Leaderboard / Storage
// ─────────────────────────────────────────────────────────────────────────────

var emptyOk   = () => Results.Ok(new { ok = true });
var emptyList = () => Results.Ok(Array.Empty<object>());

app.MapGet   ("/notifications",           () => Results.Ok(new { notifications = Array.Empty<object>(), total = 0 }));
app.MapGet   ("/notifications/{**rest}",  (string rest) => Results.Ok(new { notifications = Array.Empty<object>(), total = 0 }));
app.MapPost  ("/notifications/{**rest}",  (string rest) => emptyOk());
app.MapDelete("/notifications/{**rest}",  (string rest) => emptyOk());

app.MapGet   ("/chat/{**rest}",           (string rest) => Results.Ok(new { messages = Array.Empty<object>() }));
app.MapPost  ("/chat/{**rest}",           (string rest) => emptyOk());

app.MapGet   ("/clubs",                   emptyList);
app.MapGet   ("/clubs/{**rest}",          (string rest) => emptyOk());
app.MapPost  ("/clubs/{**rest}",          (string rest) => emptyOk());

app.MapGet   ("/commerce/{**rest}",       (string rest) => Results.Ok(new { items = Array.Empty<object>(), balance = 9999, tokens = 9999 }));
app.MapPost  ("/commerce/{**rest}",       (string rest) => emptyOk());

app.MapGet   ("/store/{**rest}",          (string rest) => Results.Ok(new { items = Array.Empty<object>() }));

app.MapGet   ("/leaderboard/{**rest}",    (string rest) => Results.Ok(new { entries = Array.Empty<object>(), total = 0 }));

app.MapGet   ("/storage/{**rest}",        (string rest) => emptyOk());
app.MapPost  ("/storage/{**rest}",        (string rest) => emptyOk());
app.MapMethods("/storage/{**rest}",       new[]{"PUT"}, (string rest) => emptyOk());
app.MapDelete("/storage/{**rest}",        (string rest) => emptyOk());

app.MapGet   ("/link/{**rest}",           (string rest) => Results.Ok(new { url = BaseUrl() }));
app.MapPost  ("/link/{**rest}",           (string rest) => emptyOk());

app.MapGet   ("/roomcomments/{**rest}",   (string rest) => Results.Ok(new { comments = Array.Empty<object>(), total = 0 }));
app.MapPost  ("/roomcomments/{**rest}",   (string rest) => emptyOk());
app.MapDelete("/roomcomments/{**rest}",   (string rest) => emptyOk());

// Analytics — swallow silently
app.MapPost("/httpapi",                   emptyOk);
app.MapPost("/identify",                  emptyOk);
app.MapPost("/v1/amplitude",              emptyOk);
app.MapPost("/api/v1/amplitude",          emptyOk);
app.MapPost("/{prefix}/v1/amplitude",    (string prefix) => emptyOk());
app.MapGet ("/player/photonregionpings",  () => Results.Ok(new { regions = new { us = 10 } }));
app.MapPost("/player/photonregionpings",  emptyOk);

// Images / CDN — return 204 (no content)
app.MapGet("/images/{**rest}", (string rest) => Results.NoContent());
app.MapGet("/cdn/{**rest}",    (string rest) => Results.NoContent());

// Consumables / Items
app.MapGet  ("/api/consumables/{**rest}", (string rest) => Results.Ok(new { items = Array.Empty<object>() }));
app.MapPost ("/api/consumables/{**rest}", (string rest) => emptyOk());
app.MapMethods("/api/consumables/{**rest}", new[]{"PATCH"}, (string rest) => emptyOk());

// ─────────────────────────────────────────────────────────────────────────────
// Catch-all fallback
// ─────────────────────────────────────────────────────────────────────────────

app.Use(async (ctx, next) =>
{
    await next();
    if (ctx.Response.StatusCode == 404)
    {
        ctx.Response.StatusCode  = 200;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"ok\":true,\"stub\":true}");
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Start Photon UDP servers
// ─────────────────────────────────────────────────────────────────────────────

var logger   = app.Services.GetRequiredService<ILoggerFactory>();
var nsLogger = logger.CreateLogger<PhotonServer>();

var photonNs = new PhotonServer(
    photonNsPort, PhotonServer.ServerMode.NameServer,
    publicDomain, photonMPort, nsLogger);

var photonMaster = new PhotonServer(
    photonMPort, PhotonServer.ServerMode.MasterServer,
    publicDomain, photonMPort, nsLogger);

photonNs.Start();
photonMaster.Start();

Console.WriteLine(
    "=========================================================\n" +
    "  Rec Room Preservation Server  --  Running              \n" +
    "  © 2025 Idontanything53. All Rights Reserved.           \n" +
    "=========================================================\n" +
    $"  Public Domain : {publicDomain}\n" +
    $"  REST API      : http://0.0.0.0:{apiPort}\n" +
    $"  Photon NS     : UDP  :{photonNsPort}\n" +
    $"  Photon Master : UDP  :{photonMPort}\n" +
    $"  Fingerprint   : {Watermark.Fingerprint}\n" +
    $"  Integrity     : {(Watermark.Degraded ? "DEGRADED" : "OK")}\n" +
    "=========================================================\n" +
    $"  Players should patch to: {publicDomain}\n" +
    "  Press Ctrl+C to stop."
);

await app.RunAsync();
