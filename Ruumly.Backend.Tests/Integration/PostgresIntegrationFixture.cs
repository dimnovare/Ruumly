using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ruumly.Backend.Data;
using Xunit;

namespace Ruumly.Backend.Tests.Integration;

/// <summary>
/// Spins up an ISOLATED Postgres database (created fresh, migrated, dropped on
/// dispose) so tests run against the real Npgsql provider — catching translation
/// bugs that EF InMemory silently passes (enum string-comparison, ILike/tsvector,
/// correlated subqueries, unique constraints). This is the gap that let the
/// lowercase-ListingType prod bug through.
///
/// Connection: env RUUMLY_TEST_PG (admin/maintenance DB) or the local Docker
/// Postgres on 5433. If the server is unreachable, <see cref="Available"/> is false
/// and the dependent tests no-op (so CI without Postgres stays green).
/// </summary>
public sealed class PostgresIntegrationFixture : IAsyncLifetime
{
    private const string DefaultAdminConn =
        "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres";

    private string _adminConn = DefaultAdminConn;
    private string _dbName = "";

    public bool Available { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _adminConn = Environment.GetEnvironmentVariable("RUUMLY_TEST_PG") ?? DefaultAdminConn;
        _dbName = "ruumly_it_" + Guid.NewGuid().ToString("N")[..12];

        try
        {
            await using (var admin = new NpgsqlConnection(_adminConn))
            {
                await admin.OpenAsync();
                await using var create = admin.CreateCommand();
                create.CommandText = $"CREATE DATABASE \"{_dbName}\"";
                await create.ExecuteNonQueryAsync();
            }

            ConnectionString = new NpgsqlConnectionStringBuilder(_adminConn) { Database = _dbName }.ConnectionString;

            await using (var db = NewContext())
                await db.Database.MigrateAsync();

            Available = true;
        }
        catch
        {
            // No reachable Postgres (e.g. CI without a DB) — dependent tests skip.
            Available = false;
        }
    }

    public RuumlyDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new RuumlyDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (!Available) return;
        try
        {
            await using var admin = new NpgsqlConnection(_adminConn);
            await admin.OpenAsync();

            await using (var terminate = admin.CreateCommand())
            {
                terminate.CommandText =
                    $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                    $"WHERE datname = '{_dbName}' AND pid <> pg_backend_pid()";
                await terminate.ExecuteNonQueryAsync();
            }
            await using (var drop = admin.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\"";
                await drop.ExecuteNonQueryAsync();
            }
        }
        catch { /* best-effort cleanup */ }
    }
}

[CollectionDefinition("Postgres integration")]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresIntegrationFixture> { }
