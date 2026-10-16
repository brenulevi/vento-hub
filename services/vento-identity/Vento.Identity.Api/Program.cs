using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException("Defina ConnectionStrings__Identity para conectar ao PostgreSQL.");

builder.Services.AddDbContext<VentoIdentityDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<LocalImageStorage>();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<DesktopCodeStore>();
builder.Services.AddIdentityApiEndpoints<IdentityUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-@";
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.SignIn.RequireConfirmedEmail = false;
}).AddEntityFrameworkStores<VentoIdentityDbContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<VentoIdentityDbContext>().Database.MigrateAsync();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<LocalImageStorage>().MigrateLegacyImagesAsync(
        scope.ServiceProvider.GetRequiredService<VentoIdentityDbContext>());
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/apps", () => Results.Ok(new[]
{
    new VentoApplication(
        "vento-sharing",
        "Vento Sharing",
        "Compartilhe tela e áudio com seus amigos em uma sala privada. Uma pessoa transmite por vez; até seis participam.",
        "development")
}));
app.MapPost("/auth/register", async (RegisterRequest request, UserManager<IdentityUser> users) =>
{
    var username = request.Username?.Trim() ?? string.Empty;
    var email = request.Email?.Trim() ?? string.Empty;
    if (!Regex.IsMatch(username, "^[A-Za-z0-9][A-Za-z0-9._-]{2,31}$", RegexOptions.CultureInvariant))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["Use de 3 a 32 caracteres: letras, números, ponto, _ ou -. Comece com letra ou número."] });
    if (!new EmailAddressAttribute().IsValid(email))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Informe um e-mail válido."] });

    var result = await users.CreateAsync(new IdentityUser { UserName = username, Email = email }, request.Password ?? string.Empty);
    return result.Succeeded
        ? Results.Ok()
        : Results.ValidationProblem(result.Errors.GroupBy(error => error.Code)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
});
app.MapPost("/auth/login", async (LoginRequest request, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    var identifier = request.Identifier?.Trim() ?? request.Email?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(request.Password))
        return Results.Unauthorized();

    var account = identifier.Contains('@')
        ? await users.FindByEmailAsync(identifier)
        : await users.FindByNameAsync(identifier);
    if (account is null || !(await signIn.CheckPasswordSignInAsync(account, request.Password, lockoutOnFailure: true)).Succeeded)
        return Results.Unauthorized();

    return Results.SignIn(await signIn.CreateUserPrincipalAsync(account), authenticationScheme: IdentityConstants.BearerScheme);
});
app.MapGet("/auth/me", async (System.Security.Claims.ClaimsPrincipal principal, UserManager<IdentityUser> users) =>
{
    var account = await users.GetUserAsync(principal);
    return account is null ? Results.Unauthorized() : Results.Ok(new { username = account.UserName, email = account.Email });
}).RequireAuthorization();
app.MapPost("/auth/desktop/code", async (DesktopCodeRequest request, System.Security.Claims.ClaimsPrincipal principal,
    UserManager<IdentityUser> users, DesktopCodeStore codes) =>
{
    if (request.CodeChallenge is null || !Regex.IsMatch(request.CodeChallenge, "^[A-Za-z0-9_-]{43}$"))
        return Results.BadRequest();
    var account = await users.GetUserAsync(principal);
    return account is null ? Results.Unauthorized() : Results.Ok(new { code = codes.Issue(account.Id, request.CodeChallenge) });
}).RequireAuthorization();
app.MapPost("/auth/desktop/token", async (DesktopTokenRequest request, UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn, DesktopCodeStore codes) =>
{
    var userId = codes.Consume(request.Code, request.CodeVerifier);
    if (userId is null)
        return Results.Unauthorized();
    var account = await users.FindByIdAsync(userId);
    return account is null ? Results.Unauthorized() :
        Results.SignIn(await signIn.CreateUserPrincipalAsync(account), authenticationScheme: IdentityConstants.BearerScheme);
});
app.MapRoomEndpoints();
app.MapFriendEndpoints();
app.MapImageEndpoints();
app.MapAccountProfileEndpoints();
app.MapMediaEndpoints();

app.Run();

public sealed class VentoIdentityDbContext(DbContextOptions<VentoIdentityDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<VentoRoom> Rooms => Set<VentoRoom>();
    public DbSet<VentoRoomMembership> RoomMemberships => Set<VentoRoomMembership>();
    public DbSet<VentoFriend> Friends => Set<VentoFriend>();
    public DbSet<VentoAccountImage> AccountImages => Set<VentoAccountImage>();
    public DbSet<VentoRoomImage> RoomImages => Set<VentoRoomImage>();
    public DbSet<VentoUserPreference> UserPreferences => Set<VentoUserPreference>();
    public DbSet<VentoRoomBroadcast> RoomBroadcasts => Set<VentoRoomBroadcast>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<VentoRoom>(room =>
        {
            room.HasKey(item => item.Id);
            room.HasIndex(item => item.InviteCode).IsUnique();
            room.Property(item => item.Name).HasMaxLength(80);
            room.Property(item => item.InviteCode).HasMaxLength(12);
            room.HasOne<IdentityUser>().WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Restrict);
            room.HasOne(item => item.Image).WithOne().HasForeignKey<VentoRoomImage>(image => image.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<VentoRoomMembership>(membership =>
        {
            membership.HasKey(item => new { item.RoomId, item.UserId });
            membership.HasOne(item => item.Room).WithMany(room => room.Memberships)
                .HasForeignKey(item => item.RoomId).OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<IdentityUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<VentoFriend>(friend =>
        {
            friend.HasKey(item => new { item.UserId, item.FriendId });
            friend.HasOne<IdentityUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            friend.HasOne<IdentityUser>().WithMany().HasForeignKey(item => item.FriendId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<VentoAccountImage>(image =>
        {
            image.HasKey(item => item.UserId);
            image.Property(item => item.ContentType).HasMaxLength(20);
            image.Property(item => item.StorageKey).HasMaxLength(100);
            image.Property(item => item.LegacyData).HasColumnName("Data");
            image.HasOne<IdentityUser>().WithOne().HasForeignKey<VentoAccountImage>(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<VentoRoomImage>(image =>
        {
            image.HasKey(item => item.RoomId);
            image.Property(item => item.ContentType).HasMaxLength(20);
            image.Property(item => item.StorageKey).HasMaxLength(100);
            image.Property(item => item.LegacyData).HasColumnName("Data");
        });
        builder.Entity<VentoUserPreference>(preference =>
        {
            preference.HasKey(item => item.UserId);
            preference.Property(item => item.ThemeId).HasMaxLength(20);
            preference.HasOne<IdentityUser>().WithOne().HasForeignKey<VentoUserPreference>(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<VentoRoomBroadcast>(broadcast =>
        {
            broadcast.HasKey(item => item.RoomId);
            broadcast.Property(item => item.SessionId).IsRequired();
            broadcast.HasOne<VentoRoom>().WithOne().HasForeignKey<VentoRoomBroadcast>(item => item.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            broadcast.HasOne<IdentityUser>().WithMany().HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

public sealed record VentoApplication(string Id, string Name, string Description, string Status);
public sealed record RegisterRequest(string? Username, string? Email, string? Password);
public sealed record LoginRequest(string? Identifier, string? Email, string? Password);
public sealed record DesktopCodeRequest(string? CodeChallenge);
public sealed record DesktopTokenRequest(string? Code, string? CodeVerifier);

public sealed class DesktopCodeStore
{
    private readonly Dictionary<string, (string UserId, string Challenge, DateTimeOffset Expires)> _codes = [];
    private readonly object _gate = new();

    public string Issue(string userId, string challenge)
    {
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        lock (_gate)
        {
            foreach (var expired in _codes.Where(item => item.Value.Expires <= DateTimeOffset.UtcNow)
                         .Select(item => item.Key).ToArray())
                _codes.Remove(expired);
            _codes.Add(code, (userId, challenge, DateTimeOffset.UtcNow.AddMinutes(2)));
        }
        return code;
    }

    public string? Consume(string? code, string? verifier)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(verifier) || verifier.Length is < 43 or > 128)
            return null;
        lock (_gate)
        {
            if (!_codes.Remove(code, out var entry) || entry.Expires <= DateTimeOffset.UtcNow)
                return null;
            var computed = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(entry.Challenge))
                ? entry.UserId : null;
        }
    }
}
