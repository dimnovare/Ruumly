using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Tests;

/// <summary>
/// GET /api/admin/leads/{id}/photos/{index} — the founder's view of what the
/// customer actually attached.
///
/// The photos were reachable by the PROVIDER (through their quote token) and by
/// nobody else, which left the one person running the match loop by hand unable
/// to judge whether a request could be quoted from at all. This pins the two
/// things that make the endpoint safe to add: the index is resolved against the
/// lead's OWN list (so the URL can never name an arbitrary private object), and
/// the route is Admin-only, unlike its tokenized public twin.
/// </summary>
public class AdminLeadPhotoTests
{
    /// <summary>A private bucket that actually holds bytes, keyed like R2's.</summary>
    private sealed class FakeStorage(Dictionary<string, byte[]> objects) : IStorageService
    {
        public List<string> Requested { get; } = [];

        public Task<byte[]?> DownloadPrivateAsync(string key)
        {
            Requested.Add(key);
            return Task.FromResult(objects.GetValueOrDefault(key));
        }

        public Task<string> UploadAsync(Stream s, string f, string c) => Task.FromResult("");
        public Task<StoredObject> UploadWithKeyAsync(Stream s, string f, string c) =>
            Task.FromResult(new StoredObject("", ""));
        public Task<byte[]?> DownloadAsync(string key) => Task.FromResult<byte[]?>(null);
        public Task DeleteAsync(string publicUrl) => Task.CompletedTask;
        public Task<string> UploadPrivateAsync(Stream s, string f, string c) => Task.FromResult("");
        public Task DeletePrivateAsync(string key) => Task.CompletedTask;
    }

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Email, $"{role.ToLowerInvariant()}@ruumly.eu"),
        ], "test"));

    private static AdminLeadsController MakeAdmin(RuumlyDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal("Admin") },
            },
        };

    /// <summary>A key of exactly the shape the upload path mints.</summary>
    private static string Key(char fill) => $"{LeadPhotoNormalizer.KeyPrefix}{new string(fill, 32)}.jpg";

    private static DemandLead Lead(string? photoKeysJson) => new()
    {
        Id = Guid.NewGuid(), Email = "cust@x.ee", City = "Tallinn",
        Category = DemandLeadCategory.Moving, Language = "et", Source = "concierge",
        Status = DemandLeadStatus.New, CreatedAt = DateTime.UtcNow.AddDays(-1),
        PhotoKeysJson = photoKeysJson,
    };

    private static string KeysJson(params string[] keys) => JsonSerializer.Serialize(keys);

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)!.GetValue(o);

    // ─── Index-addressed fetch ────────────────────────────────────────────────

    [Fact]
    public async Task GetLeadPhoto_ServesThePhotoAtThatIndex()
    {
        var db      = TestDbContext.Create();
        var first   = Key('a');
        var second  = Key('b');
        var lead    = Lead(KeysJson(first, second));
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var storage = new FakeStorage(new()
        {
            [first]  = Encoding.UTF8.GetBytes("first-photo"),
            [second] = Encoding.UTF8.GetBytes("second-photo"),
        });

        var result = await MakeAdmin(db).GetLeadPhoto(lead.Id, 1, storage, default);

        var file = result.Should().BeOfType<FileContentResult>(
            "the admin needs the bytes, not a URL — the bucket is private").Subject;
        Encoding.UTF8.GetString(file.FileContents).Should().Be("second-photo",
            "index 1 is the SECOND key on this lead's own list");
        file.ContentType.Should().Be("image/jpeg",
            "everything this feature stores is re-encoded JPEG, so the type is known");
        storage.Requested.Should().ContainSingle().Which.Should().Be(second);
    }

    [Fact]
    public async Task GetLeadPhoto_ResolvesTheIndexAgainstThatLeadsOwnList()
    {
        // The whole point of index-addressing: the id in the URL decides which
        // private objects are reachable. Another lead's photo must be invisible
        // even though it sits in the same bucket.
        var db     = TestDbContext.Create();
        var mine   = Key('c');
        var theirs = Key('d');
        var lead   = Lead(KeysJson(mine));
        db.DemandLeads.AddRange(lead, Lead(KeysJson(theirs)));
        await db.SaveChangesAsync();

        var storage = new FakeStorage(new()
        {
            [mine]   = Encoding.UTF8.GetBytes("mine"),
            [theirs] = Encoding.UTF8.GetBytes("theirs"),
        });

        var result = await MakeAdmin(db).GetLeadPhoto(lead.Id, 0, storage, default);

        Encoding.UTF8.GetString(result.Should().BeOfType<FileContentResult>().Subject.FileContents)
            .Should().Be("mine");
        storage.Requested.Should().NotContain(theirs,
            "an index is never a key — it cannot address an object this lead does not carry");
    }

    [Fact]
    public async Task GetLeadPhoto_SetsPrivateNoStore()
    {
        var db   = TestDbContext.Create();
        var key  = Key('e');
        var lead = Lead(KeysJson(key));
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var controller = MakeAdmin(db);
        await controller.GetLeadPhoto(lead.Id, 0, new FakeStorage(new() { [key] = [1, 2, 3] }), default);

        controller.Response.Headers.CacheControl.ToString().Should().Be("private, no-store",
            "a picture of somebody's home must not linger in a shared cache");
    }

    // ─── 404s ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]     // one past the end
    [InlineData(7)]     // past the per-lead cap
    [InlineData(-1)]    // negative
    public async Task GetLeadPhoto_OutOfRangeIndex_Returns404(int index)
    {
        var db   = TestDbContext.Create();
        var key  = Key('f');
        var lead = Lead(KeysJson(key));
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var storage = new FakeStorage(new() { [key] = [1] });

        var result = await MakeAdmin(db).GetLeadPhoto(lead.Id, index, storage, default);

        result.Should().BeOfType<NotFoundResult>();
        storage.Requested.Should().BeEmpty("an out-of-range index must not reach the bucket at all");
    }

    [Fact]
    public async Task GetLeadPhoto_UnknownLead_Returns404()
    {
        var db = TestDbContext.Create();
        db.DemandLeads.Add(Lead(KeysJson(Key('a'))));
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db)
            .GetLeadPhoto(Guid.NewGuid(), 0, new FakeStorage([]), default);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLeadPhoto_LeadWithNoPhotos_Returns404()
    {
        var db   = TestDbContext.Create();
        var lead = Lead(null);
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).GetLeadPhoto(lead.Id, 0, new FakeStorage([]), default);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLeadPhoto_ObjectMissingFromTheBucket_Returns404()
    {
        // The 30-day purge (BackgroundCleanupService) deletes the objects; a lead
        // whose row still names them must degrade to a broken tile, not a 500.
        var db   = TestDbContext.Create();
        var lead = Lead(KeysJson(Key('a')));
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).GetLeadPhoto(lead.Id, 0, new FakeStorage([]), default);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ─── A malformed column is data, not an outage ────────────────────────────

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    // Right prefix, forged shape — and the classic: pointing a lead at a signed
    // contract to read it back through whichever endpoint serves the lead.
    [InlineData("[\"contracts/signed-contract-2026.pdf\"]")]
    [InlineData("[\"lead-photos/../../etc/passwd\"]")]
    public async Task GetLeadPhoto_MalformedOrForgedKeys_YieldNoPhotos(string photoKeysJson)
    {
        var db   = TestDbContext.Create();
        var lead = Lead(photoKeysJson);
        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        var storage = new FakeStorage(new()
        {
            ["contracts/signed-contract-2026.pdf"] = Encoding.UTF8.GetBytes("PII"),
        });

        var act = async () => await MakeAdmin(db).GetLeadPhoto(lead.Id, 0, storage, default);

        var result = await act.Should().NotThrowAsync(
            "photos are an optional extra — a bad column must never take down the workspace");
        result.Subject.Should().BeOfType<NotFoundResult>();
        storage.Requested.Should().BeEmpty("nothing that was not minted here is addressable");
    }

    [Fact]
    public async Task GetLeads_MalformedKeys_ReportZeroPhotosInsteadOfThrowing()
    {
        var db = TestDbContext.Create();
        db.DemandLeads.Add(Lead("not json at all"));
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).GetLeads();

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var item = ((IEnumerable<object>)Prop(payload, "items")!).Single();
        Prop(item, "photoCount").Should().Be(0);
    }

    // ─── The count the list badges on ─────────────────────────────────────────

    [Fact]
    public async Task GetLeads_ReportsHowManyPhotosEachLeadCarries()
    {
        var db = TestDbContext.Create();
        var withPhotos = Lead(KeysJson(Key('a'), Key('b'), Key('c')));
        var without    = Lead(null);
        without.CreatedAt = withPhotos.CreatedAt.AddDays(-1);   // newest-first ordering
        db.DemandLeads.AddRange(withPhotos, without);
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).GetLeads();

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var items = ((IEnumerable<object>)Prop(payload, "items")!).ToList();
        Prop(items[0], "photoCount").Should().Be(3);
        Prop(items[1], "photoCount").Should().Be(0,
            "a lead with no photos must render nothing, not an empty gallery");
    }

    [Fact]
    public async Task GetLeads_NeverSendsTheStorageKeysToTheBrowser()
    {
        // The count is all the browser is trusted with: a key is an address in
        // the private bucket, and publishing one invites exactly the probing that
        // keeping the bucket private exists to prevent.
        var db = TestDbContext.Create();
        db.DemandLeads.Add(Lead(KeysJson(Key('a'))));
        await db.SaveChangesAsync();

        var result = await MakeAdmin(db).GetLeads();

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var item = ((IEnumerable<object>)Prop(payload, "items")!).Single();
        JsonSerializer.Serialize(item).Should().NotContain(LeadPhotoNormalizer.KeyPrefix);
    }

    // ─── Authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLeadPhoto_RefusesAnUnauthenticatedCaller()
    {
        var method = typeof(AdminLeadsController).GetMethod(nameof(AdminLeadsController.GetLeadPhoto))!;
        var authData = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(typeof(AdminLeadsController).GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .ToList();

        method.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(inherit: true)
            .Should().BeEmpty(
                "the tokenized quote route is the ONLY anonymous way to a customer's photos");
        authData.Should().NotBeEmpty();

        // Evaluate the real policy the attributes produce rather than trusting the string.
        await using var services = new ServiceCollection()
            .AddLogging().AddAuthorization().BuildServiceProvider();
        var policy = (await AuthorizationPolicy.CombineAsync(
            services.GetRequiredService<IAuthorizationPolicyProvider>(), authData))!;
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        (await authorization.AuthorizeAsync(anonymous, null, policy))
            .Succeeded.Should().BeFalse("these are pictures of somebody's home");
        (await authorization.AuthorizeAsync(Principal("Provider"), null, policy))
            .Succeeded.Should().BeFalse("a partner reaches photos through their own quote token, not this route");
        (await authorization.AuthorizeAsync(Principal("Customer"), null, policy))
            .Succeeded.Should().BeFalse("no customer may read another customer's request");
        (await authorization.AuthorizeAsync(Principal("Admin"), null, policy))
            .Succeeded.Should().BeTrue("ops runs the match loop out of this workspace");
    }
}
