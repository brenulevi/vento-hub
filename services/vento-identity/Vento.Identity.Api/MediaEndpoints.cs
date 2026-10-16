using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public sealed record BroadcastPresenceRequest(Guid SessionId, bool IsPublishing);

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        app.MapPost("/rooms/{id:guid}/broadcast/presence", async (Guid id, BroadcastPresenceRequest update, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var updated = await db.RoomBroadcasts
                .Where(item => item.RoomId == id && item.UserId == userId && item.SessionId == update.SessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.LastSeenAt, DateTimeOffset.UtcNow)
                    .SetProperty(item => item.IsPublishing, update.IsPublishing));
            return updated == 1 ? Results.NoContent() : Results.Conflict("A reserva da transmissão expirou.");
        }).RequireAuthorization();

        app.MapDelete("/rooms/{id:guid}/broadcast/presence", async (Guid id, [FromBody] BroadcastPresenceRequest update, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            await db.RoomBroadcasts.Where(item => item.RoomId == id && item.UserId == userId
                && item.SessionId == update.SessionId).ExecuteDeleteAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/rooms/{id:guid}/media-token", async (Guid id, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db, IConfiguration configuration,
            IWebHostEnvironment environment) =>
            await IssueTokenAsync(id, false, principal, users, db, configuration, environment))
            .RequireAuthorization();

        app.MapPost("/rooms/{id:guid}/publisher-token", async (Guid id, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db, IConfiguration configuration,
            IWebHostEnvironment environment) =>
            await IssueTokenAsync(id, true, principal, users, db, configuration, environment))
            .RequireAuthorization();
    }

    private static async Task<IResult> IssueTokenAsync(Guid id, bool publish, ClaimsPrincipal principal,
        UserManager<IdentityUser> users, VentoIdentityDbContext db, IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        var room = await db.Rooms.Where(item => item.Id == id)
            .Select(item => new { item.Id, item.Name }).SingleOrDefaultAsync();
        if (room is null || !await db.RoomMemberships.AnyAsync(item => item.RoomId == id && item.UserId == user.Id))
            return Results.NotFound();
        var key = configuration["LiveKit:ApiKey"];
        var secret = configuration["LiveKit:ApiSecret"];
        var url = configuration["LiveKit:PublicUrl"];
        if (environment.IsDevelopment())
        {
            key ??= "devkey";
            secret ??= "secret";
            url ??= "ws://127.0.0.1:7880";
        }
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(url) || (!environment.IsDevelopment() && secret.Length < 32))
            return Results.Problem("Configure LiveKit:ApiKey, LiveKit:ApiSecret e LiveKit:PublicUrl no servidor.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        Guid? sessionId = null;
        if (publish)
        {
            sessionId = Guid.NewGuid();
            var nowUtc = DateTimeOffset.UtcNow;
            var cutoff = nowUtc.AddSeconds(-45);
            // The room key makes acquisition atomic even when two members request a token together.
            var acquired = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "RoomBroadcasts" ("RoomId", "UserId", "LastSeenAt", "SessionId", "IsPublishing")
                VALUES ({id}, {user.Id}, {nowUtc}, {sessionId.Value}, FALSE)
                ON CONFLICT ("RoomId") DO UPDATE SET
                    "UserId" = EXCLUDED."UserId",
                    "LastSeenAt" = EXCLUDED."LastSeenAt",
                    "SessionId" = EXCLUDED."SessionId",
                    "IsPublishing" = FALSE
                WHERE "RoomBroadcasts"."LastSeenAt" < {cutoff}
                """);
            if (acquired == 0)
                return Results.Conflict("Outra pessoa já está iniciando ou compartilhando a tela nesta sala.");
        }

        var canPublish = publish;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = SignToken(key, secret, new
        {
            iss = key,
            sub = canPublish ? $"{room.Id:N}:publisher" : $"{user.Id}:viewer:{Guid.NewGuid():N}",
            name = user.UserName,
            iat = now,
            nbf = now,
            exp = now + (canPublish ? 60 : 5 * 60),
            video = new
            {
                roomJoin = true,
                room = room.Id.ToString("N"),
                canSubscribe = true,
                canPublish,
                canPublishData = false,
                canPublishSources = canPublish ? new[] { "screen_share" } : Array.Empty<string>()
            }
        });
        return Results.Ok(new { url, token, roomName = room.Name, canPublish, sessionId });
    }

    private static string SignToken(string key, string secret, object claims)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        var body = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes(body));
        return $"{body}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
