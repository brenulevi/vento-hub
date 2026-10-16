using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class VentoIdentityDbContextFactory : IDesignTimeDbContextFactory<VentoIdentityDbContext>
{
    public VentoIdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Identity")
            ?? "Host=localhost;Port=55432;Database=vento_identity;Username=vento_dev;Password=vento_local_dev";
        var options = new DbContextOptionsBuilder<VentoIdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new VentoIdentityDbContext(options);
    }
}
