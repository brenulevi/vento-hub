using Microsoft.EntityFrameworkCore;

public sealed class LocalImageStorage(IConfiguration configuration, IWebHostEnvironment environment)
{
    private readonly string _root = Path.GetFullPath(configuration["Media:StoragePath"]
        ?? Path.Combine(environment.ContentRootPath, "data", "media"));

    public async Task<string> SaveAsync(string category, byte[] data, string contentType)
    {
        var directory = Path.Combine(_root, category);
        Directory.CreateDirectory(directory);
        var extension = contentType == "image/png" ? ".png" : ".jpg";
        var name = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(directory, name);
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(data);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return $"{category}/{name}";
    }

    public string? GetPath(string? key)
    {
        if (key is null) return null;
        var parts = key.Split('/');
        if (parts.Length != 2 || parts[0] is not ("accounts" or "rooms")
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(parts[1]), "N", out _)
            || Path.GetExtension(parts[1]) is not (".png" or ".jpg"))
            return null;
        return Path.Combine(_root, parts[0], parts[1]);
    }

    public void Delete(string? key)
    {
        var path = GetPath(key);
        if (path is not null) File.Delete(path);
    }

    public async Task MigrateLegacyImagesAsync(VentoIdentityDbContext db)
    {
        foreach (var image in await db.AccountImages.Where(item => item.StorageKey == null && item.LegacyData != null).ToListAsync())
        {
            var key = await SaveAsync("accounts", image.LegacyData!, image.ContentType);
            image.StorageKey = key;
            image.LegacyData = null;
            try { await db.SaveChangesAsync(); }
            catch { Delete(key); throw; }
        }
        foreach (var image in await db.RoomImages.Where(item => item.StorageKey == null && item.LegacyData != null).ToListAsync())
        {
            var key = await SaveAsync("rooms", image.LegacyData!, image.ContentType);
            image.StorageKey = key;
            image.LegacyData = null;
            try { await db.SaveChangesAsync(); }
            catch { Delete(key); throw; }
        }
    }
}
