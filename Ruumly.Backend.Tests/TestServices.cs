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

    /// <summary>
    /// The real provider-outcome notifier, wired to the same DbContext and email
    /// queue as the controller under test. Deliberately NOT a no-op fake: the
    /// "offer sent" and "customer chose" paths now enqueue provider mail, and a
    /// test that stubs it out would stop noticing whether they do.
    /// </summary>
    public static IProviderOutcomeNotifier OutcomeNotifier(
        RuumlyDbContext db, IBackgroundEmailQueue queue) =>
        new ProviderOutcomeNotifier(db, queue, NullLogger<ProviderOutcomeNotifier>.Instance);

    /// <summary>
    /// Storage that stores nothing. Several controllers and the cleanup job now
    /// take IStorageService only because of the request-photo feature; tests that
    /// exercise anything else should not need a bucket to do it.
    /// </summary>
    public sealed class NoStorage : IStorageService
    {
        public Task<string> UploadAsync(Stream s, string f, string c) => Task.FromResult("");
        public Task<StoredObject> UploadWithKeyAsync(Stream s, string f, string c) =>
            Task.FromResult(new StoredObject("", ""));
        public Task<byte[]?> DownloadAsync(string key) => Task.FromResult<byte[]?>(null);
        public Task DeleteAsync(string publicUrl) => Task.CompletedTask;
        public Task<string> UploadPrivateAsync(Stream s, string f, string c) => Task.FromResult("");
        public Task<byte[]?> DownloadPrivateAsync(string key) => Task.FromResult<byte[]?>(null);
        public Task DeletePrivateAsync(string key) => Task.CompletedTask;
    }
}