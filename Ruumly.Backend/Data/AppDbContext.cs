using Microsoft.EntityFrameworkCore;

namespace Ruumly.Backend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // DbSets will be added here as models are scaffolded
}
