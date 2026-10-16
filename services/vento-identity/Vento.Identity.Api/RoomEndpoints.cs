using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public sealed class VentoRoom
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string InviteCode { get; set; }
    public required string OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<VentoRoomMembership> Memberships { get; set; } = [];
    public VentoRoomImage? Image { get; set; }
}

public sealed class VentoRoomMembership
{
    public Guid RoomId { get; set; }
    public required string UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LastVisitedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public VentoRoom Room { get; set; } = null!;
}

public sealed class VentoRoomBroadcast
{
    public Guid RoomId { get; set; }
    public required string UserId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public Guid SessionId { get; set; }
    public bool IsPublishing { get; set; }
}

public sealed record CreateRoomRequest(string? Name);
public sealed record JoinRoomRequest(string? InviteCode);
public sealed record RoomResponse(Guid Id, string Name, string InviteCode, bool IsOwner,
    int ParticipantCount, int OnlineCount, DateTimeOffset? LastVisitedAt,
    DateTimeOffset? ImageUpdatedAt = null);
public sealed record RoomParticipantResponse(string Username, bool IsOwner, bool IsOnline, bool IsSharing);

public static class RoomEndpoints
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static void MapRoomEndpoints(this WebApplication app)
    {
        var rooms = app.MapGroup("/rooms").RequireAuthorization();

        rooms.MapGet("/", async (ClaimsPrincipal principal, UserManager<IdentityUser> users,
            VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var onlineSince = DateTimeOffset.UtcNow.AddSeconds(-45);
            var result = await db.RoomMemberships.Where(member => member.UserId == userId)
                .Select(member => new RoomResponse(member.Room.Id, member.Room.Name,
                    member.Room.InviteCode, member.Room.OwnerId == userId, member.Room.Memberships.Count,
                    member.Room.Memberships.Count(participant => participant.LastSeenAt >= onlineSince),
                    member.LastVisitedAt, member.Room.Image == null ? null : member.Room.Image.UpdatedAt))
                .ToListAsync();
            return Results.Ok(result);
        });

        rooms.MapGet("/{id:guid}/participants", async (Guid id, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            if (!await db.RoomMemberships.AnyAsync(member => member.RoomId == id && member.UserId == userId))
                return Results.NotFound();

            var onlineSince = DateTimeOffset.UtcNow.AddSeconds(-45);
            var activeSharerId = await db.RoomBroadcasts.Where(item => item.RoomId == id && item.IsPublishing && item.LastSeenAt >= onlineSince)
                .Select(item => item.UserId).SingleOrDefaultAsync();
            var members = await (from member in db.RoomMemberships
                join user in db.Users on member.UserId equals user.Id
                where member.RoomId == id
                select new { member.UserId, Username = user.UserName, IsOwner = member.Room.OwnerId == member.UserId,
                    IsOnline = member.LastSeenAt >= onlineSince }).ToListAsync();
            var participants = members.Select(member => new RoomParticipantResponse(member.Username ?? "Usuário",
                    member.IsOwner, member.IsOnline, member.UserId == activeSharerId))
                .OrderByDescending(participant => participant.IsOwner)
                .ThenByDescending(participant => participant.IsOnline)
                .ThenBy(participant => participant.Username)
                .ToArray();
            return Results.Ok(participants);
        });

        rooms.MapPost("/", async (CreateRoomRequest request, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is < 3 or > 80)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Use de 3 a 80 caracteres para o nome da sala."] });

            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
            var room = new VentoRoom
            {
                Id = Guid.NewGuid(), Name = name, InviteCode = code, OwnerId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
                Memberships = [new VentoRoomMembership { UserId = userId, JoinedAt = DateTimeOffset.UtcNow }]
            };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            return Results.Created($"/rooms/{room.Id}", new RoomResponse(room.Id, room.Name, code, true, 1, 0, null));
        });

        rooms.MapPost("/join", async (JoinRoomRequest request, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var code = request.InviteCode?.Trim().ToUpperInvariant() ?? string.Empty;
            if (code.Length != 10 || code.Any(ch => !CodeAlphabet.Contains(ch)))
                return Results.BadRequest(new { message = "Código de convite inválido." });

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var room = await db.Rooms.FromSqlInterpolated($"SELECT * FROM \"Rooms\" WHERE \"InviteCode\" = {code} FOR UPDATE")
                .SingleOrDefaultAsync();
            if (room is null) return Results.NotFound(new { message = "Sala não encontrada." });
            await db.Entry(room).Collection(item => item.Memberships).LoadAsync();
            if (room.Memberships.Any(member => member.UserId == userId))
                return Results.Ok(new RoomResponse(room.Id, room.Name, code, room.OwnerId == userId,
                    room.Memberships.Count, 0, null));
            if (room.Memberships.Count >= 6)
                return Results.Conflict(new { message = "Esta sala já tem seis participantes." });

            room.Memberships.Add(new VentoRoomMembership { UserId = userId, JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new RoomResponse(room.Id, room.Name, code, false, room.Memberships.Count, 0, null));
        });

        rooms.MapPost("/{id:guid}/presence", async (Guid id, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var updated = await db.RoomMemberships.Where(member => member.RoomId == id && member.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(member => member.LastVisitedAt, now)
                    .SetProperty(member => member.LastSeenAt, now));
            return updated == 0 ? Results.NotFound() : Results.NoContent();
        });

        rooms.MapDelete("/{id:guid}/presence", async (Guid id, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            await db.RoomMemberships.Where(member => member.RoomId == id && member.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.LastSeenAt, (DateTimeOffset?)null));
            return Results.NoContent();
        });
    }
}
