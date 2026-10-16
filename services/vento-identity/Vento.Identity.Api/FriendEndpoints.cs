using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public sealed class VentoFriend
{
    public required string UserId { get; set; }
    public required string FriendId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

public sealed record AddFriendRequest(string? Username);
public sealed record FriendResponse(string Username);

public static class FriendEndpoints
{
    public static void MapFriendEndpoints(this WebApplication app)
    {
        var friends = app.MapGroup("/friends").RequireAuthorization();

        friends.MapGet("/", async (ClaimsPrincipal principal, UserManager<IdentityUser> users,
            VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var result = await (from friend in db.Friends
                join account in db.Users on friend.FriendId equals account.Id
                where friend.UserId == userId
                orderby account.UserName
                select new FriendResponse(account.UserName!)).ToListAsync();
            return Results.Ok(result);
        });

        friends.MapPost("/", async (AddFriendRequest request, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var username = request.Username?.Trim() ?? string.Empty;
            if (username.Length is < 3 or > 32)
                return Results.BadRequest(new { message = "Informe o username do amigo." });

            var account = await users.FindByNameAsync(username);
            if (account is null) return Results.NotFound(new { message = "Usuário não encontrado." });
            if (account.Id == userId) return Results.BadRequest(new { message = "Você não pode adicionar a si mesmo." });

            if (!await db.Friends.AnyAsync(friend => friend.UserId == userId && friend.FriendId == account.Id))
            {
                db.Friends.Add(new VentoFriend
                {
                    UserId = userId, FriendId = account.Id, AddedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }
            return Results.Ok(new FriendResponse(account.UserName!));
        });
    }
}
