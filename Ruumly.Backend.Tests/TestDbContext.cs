using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Tests;

/// <summary>
/// DbContext subclass for unit tests using the InMemory provider.
/// Ignores NpgsqlTsVector properties that are unsupported by InMemory.
/// </summary>
internal sealed class TestDbContext(DbContextOptions<RuumlyDbContext> options)
    : RuumlyDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<Listing>().Ignore(l => l.SearchVector);
    }

    public static TestDbContext Create() =>
        new(new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
