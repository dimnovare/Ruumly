using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ruumly.Backend.Data;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations add …).
/// The connection string is only needed at runtime; a placeholder is sufficient here.
/// </summary>
public class RuumlyDbContextFactory : IDesignTimeDbContextFactory<RuumlyDbContext>
{
    public RuumlyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseNpgsql("Host=localhost;Database=ruumly_design_time;Username=postgres;Password=postgres")
            .Options;

        return new RuumlyDbContext(options);
    }
}
