using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ruumly.Backend.Data;
using Ruumly.Backend.Services.Implementations;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// Real service instances for controller tests — controllers under test exercise
/// the production code path (e.g. the actual provider-outreach sender), not a stub.
/// </summary>
internal static class TestServices
{
    public static IConfiguration Config(string? appUrl = "https://ruumly.eu") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppUrl"] = appUrl })
            .Build();

    public static IConciergeOutreachService Outreach(
        RuumlyDbContext db, IBackgroundEmailQueue queue, IConfiguration? config = null) =>
        new ConciergeOutreachService(
            db, queue, config ?? new ConfigurationBuilder().Build(),
            NullLogger<ConciergeOutreachService>.Instance);
}
