using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public sealed class VentoUserPreference
{
    public required string UserId { get; set; }
    public required string ThemeId { get; set; }
}

public sealed record UpdateUsernameRequest(string? Username);
public sealed record UpdateThemeRequest(string? ThemeId);

public static class AccountProfileEndpoints
{
    private static readonly string[] Themes = ["graphite", "aurora", "ocean", "forest", "sunset"];

    public static void MapAccountProfileEndpoints(this WebApplication app)
    {
        var profile = app.MapGroup("/account/profile").RequireAuthorization();
        profile.MapGet("/", async (ClaimsPrincipal principal, UserManager<IdentityUser> users,
            VentoIdentityDbContext db) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var theme = await db.UserPreferences.Where(item => item.UserId == user.Id)
                .Select(item => item.ThemeId).SingleOrDefaultAsync() ?? "graphite";
            return Results.Ok(new { username = user.UserName, email = user.Email, themeId = theme });
        });

        profile.MapPut("/username", async (UpdateUsernameRequest request, ClaimsPrincipal principal,
            UserManager<IdentityUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var username = request.Username?.Trim() ?? string.Empty;
            if (!Regex.IsMatch(username, "^[A-Za-z0-9][A-Za-z0-9._-]{2,31}$", RegexOptions.CultureInvariant))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["Use de 3 a 32 caracteres: letras, números, ponto, _ ou -. Comece com letra ou número."] });
            if (string.Equals(user.UserName, username, StringComparison.Ordinal)) return Results.NoContent();
            var result = await users.SetUserNameAsync(user, username);
            return result.Succeeded ? Results.NoContent()
                : Results.ValidationProblem(result.Errors.GroupBy(error => error.Code)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        });

        profile.MapPut("/theme", async (UpdateThemeRequest request, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var themeId = request.ThemeId?.Trim().ToLowerInvariant();
            if (themeId is null || !Themes.Contains(themeId))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["themeId"] = ["Escolha uma das cores disponíveis."] });
            var preference = await db.UserPreferences.FindAsync(userId);
            if (preference is null)
                db.UserPreferences.Add(new VentoUserPreference { UserId = userId, ThemeId = themeId });
            else
                preference.ThemeId = themeId;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
