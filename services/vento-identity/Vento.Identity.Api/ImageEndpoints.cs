using System.Buffers.Binary;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public sealed class VentoAccountImage
{
    public required string UserId { get; set; }
    public byte[]? LegacyData { get; set; }
    public string? StorageKey { get; set; }
    public required string ContentType { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class VentoRoomImage
{
    public Guid RoomId { get; set; }
    public byte[]? LegacyData { get; set; }
    public string? StorageKey { get; set; }
    public required string ContentType { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ImageEndpoints
{
    private const int MaxImageBytes = 2 * 1024 * 1024;
    private const int MaxDimension = 2048;

    public static void MapImageEndpoints(this WebApplication app)
    {
        var account = app.MapGroup("/account/image").RequireAuthorization();
        account.MapGet("/", async (ClaimsPrincipal principal, UserManager<IdentityUser> users,
            VentoIdentityDbContext db, LocalImageStorage storage) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var image = await db.AccountImages.FindAsync(userId);
            return ServeImage(image?.StorageKey, image?.LegacyData, image?.ContentType, storage);
        });
        account.MapMethods("/", ["HEAD"], async (ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            return await db.AccountImages.AnyAsync(image => image.UserId == userId)
                ? Results.NoContent() : Results.NotFound();
        });
        account.MapPut("/", async (HttpRequest request, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db, LocalImageStorage storage) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var upload = await ReadImageAsync(request);
            if (upload.Error is not null) return upload.Error;
            var image = await db.AccountImages.FindAsync(userId);
            var oldKey = image?.StorageKey;
            var key = await storage.SaveAsync("accounts", upload.Data!, upload.ContentType!);
            if (image is null)
            {
                image = new VentoAccountImage { UserId = userId, StorageKey = key, ContentType = upload.ContentType!, UpdatedAt = DateTimeOffset.UtcNow };
                db.AccountImages.Add(image);
            }
            else
            {
                image.StorageKey = key;
                image.LegacyData = null;
                image.ContentType = upload.ContentType!;
                image.UpdatedAt = DateTimeOffset.UtcNow;
            }
            try { await db.SaveChangesAsync(); }
            catch { storage.Delete(key); throw; }
            storage.Delete(oldKey);
            return Results.NoContent();
        });
        account.MapDelete("/", async (ClaimsPrincipal principal, UserManager<IdentityUser> users,
            VentoIdentityDbContext db, LocalImageStorage storage) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var image = await db.AccountImages.FindAsync(userId);
            if (image is not null)
            {
                db.AccountImages.Remove(image);
                await db.SaveChangesAsync();
                storage.Delete(image.StorageKey);
            }
            return Results.NoContent();
        });

        var rooms = app.MapGroup("/rooms/{id:guid}/image").RequireAuthorization();
        rooms.MapGet("/", async (Guid id, ClaimsPrincipal principal, UserManager<IdentityUser> users,
            VentoIdentityDbContext db, LocalImageStorage storage) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            if (!await db.RoomMemberships.AnyAsync(member => member.RoomId == id && member.UserId == userId))
                return Results.NotFound();
            var image = await db.RoomImages.FindAsync(id);
            return ServeImage(image?.StorageKey, image?.LegacyData, image?.ContentType, storage);
        });
        rooms.MapPut("/", async (Guid id, HttpRequest request, ClaimsPrincipal principal,
            UserManager<IdentityUser> users, VentoIdentityDbContext db, LocalImageStorage storage) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var room = await db.Rooms.FindAsync(id);
            if (room is null) return Results.NotFound();
            if (room.OwnerId != userId) return Results.Forbid();
            var upload = await ReadImageAsync(request);
            if (upload.Error is not null) return upload.Error;
            var image = await db.RoomImages.FindAsync(id);
            var oldKey = image?.StorageKey;
            var key = await storage.SaveAsync("rooms", upload.Data!, upload.ContentType!);
            if (image is null)
            {
                image = new VentoRoomImage { RoomId = id, StorageKey = key, ContentType = upload.ContentType!, UpdatedAt = DateTimeOffset.UtcNow };
                db.RoomImages.Add(image);
            }
            else
            {
                image.StorageKey = key;
                image.LegacyData = null;
                image.ContentType = upload.ContentType!;
                image.UpdatedAt = DateTimeOffset.UtcNow;
            }
            try { await db.SaveChangesAsync(); }
            catch { storage.Delete(key); throw; }
            storage.Delete(oldKey);
            return Results.NoContent();
        });
        rooms.MapDelete("/", async (Guid id, ClaimsPrincipal principal, UserManager<IdentityUser> users,
            VentoIdentityDbContext db, LocalImageStorage storage) =>
        {
            var userId = users.GetUserId(principal);
            if (userId is null) return Results.Unauthorized();
            var room = await db.Rooms.FindAsync(id);
            if (room is null) return Results.NotFound();
            if (room.OwnerId != userId) return Results.Forbid();
            var image = await db.RoomImages.FindAsync(id);
            if (image is not null)
            {
                db.RoomImages.Remove(image);
                await db.SaveChangesAsync();
                storage.Delete(image.StorageKey);
            }
            return Results.NoContent();
        });
    }

    private static IResult ServeImage(string? key, byte[]? legacyData, string? contentType, LocalImageStorage storage)
    {
        if (contentType is null) return Results.NotFound();
        var path = storage.GetPath(key);
        if (path is not null && File.Exists(path)) return Results.File(path, contentType);
        return legacyData is not null ? Results.File(legacyData, contentType) : Results.NotFound();
    }

    private static async Task<(byte[]? Data, string? ContentType, IResult? Error)> ReadImageAsync(HttpRequest request)
    {
        if (request.ContentLength > MaxImageBytes)
            return (null, null, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
        if (request.ContentType is not ("image/png" or "image/jpeg"))
            return (null, null, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));

        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
                return (null, null, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }
        var data = buffer.ToArray();
        var valid = request.ContentType == "image/png" ? ValidPng(data) : ValidJpeg(data);
        return valid
            ? (data, request.ContentType, null)
            : (null, null, Results.BadRequest(new { message = "Imagem inválida ou maior que 2048 × 2048 pixels." }));
    }

    private static bool ValidPng(byte[] data)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 57 || !data.AsSpan(0, 8).SequenceEqual(signature)
            || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)) != 13
            || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension)
            return false;
        var offset = 8;
        var hasImageData = false;
        while (offset + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
            if (length > data.Length - offset - 12) return false;
            var type = data.AsSpan(offset + 4, 4);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 8 + (int)length, 4));
            if (Crc32(data.AsSpan(offset + 4, 4 + (int)length)) != expectedCrc) return false;
            if (type.SequenceEqual("IDAT"u8)) hasImageData = true;
            if (type.SequenceEqual("IEND"u8))
                return length == 0 && hasImageData && offset + 12 == data.Length;
            offset += (int)length + 12;
        }
        return false;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xEDB88320u : 0u);
        }
        return ~crc;
    }

    private static bool ValidJpeg(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8
            || data[^2] != 0xFF || data[^1] != 0xD9) return false;
        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset++] != 0xFF) return false;
            while (offset < data.Length && data[offset] == 0xFF) offset++;
            if (offset >= data.Length) return false;
            var marker = data[offset++];
            if (marker is 0xD9 or 0xDA) return false;
            if (marker is >= 0xD0 and <= 0xD7 or 0x01) continue;
            if (offset + 2 > data.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            if (length < 2 || offset + length > data.Length) return false;
            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                if (length < 7) return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 5, 2));
                return width is > 0 and <= MaxDimension && height is > 0 and <= MaxDimension;
            }
            offset += length;
        }
        return false;
    }
}
