# Lead Operations Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Ruumly admins a guided, mobile-ready workflow for finding nearby providers, reviewing outreach, building and previewing offers, recording customer preference, and confirming a booked concierge outcome.

**Architecture:** Add backward-compatible ASP.NET Core endpoints and focused helpers for geographic provider discovery, message composition, offer projection, and lifecycle transitions. The React admin workspace consumes those contracts through focused stage components, while a shared offer presentation component keeps the admin preview and public page visually aligned. No database migration is required.

**Tech Stack:** ASP.NET Core 8, EF Core 8, PostgreSQL, xUnit, FluentAssertions, React 18, TypeScript 5, TanStack Query 5, Tailwind, Radix UI, Vitest, Playwright.

## Global Constraints

- Preserve `GET /api/admin/leads/{id}/matches` and every existing public endpoint.
- Add no database migration; all behavior uses existing `DemandLead`, `Offer`, `OfferOption`, `ProviderOutreach`, `Supplier`, `SupplierLocation`, and `Listing` data.
- Nearby provider search defaults to 25 km, clamps radius to 1..250 km, deduplicates before limiting, and never broadens the lead category implicitly.
- `category=any` is accepted only with `scope=all`; invalid combinations return HTTP 400.
- Preview endpoints have no persistence, email, read-receipt, or lifecycle side effects.
- Provider and customer preview text must come from the same composers used for actual delivery.
- Customer selection leaves the lead `Quoted`; only admin confirmation moves it to `Converted`.
- Customer-facing copy says `Request this offer` and explains that no payment or confirmed booking occurs until Ruumly confirms with the provider.
- All new visible copy must exist in et, en, ru, lv, and lt with equal translation key counts.
- Touch targets must be at least 44 px; mobile verification uses 375x812 and desktop verification uses 1440x900.
- Backend deploys before frontend. Production canary uses a dedicated test lead and must not mutate the first real lead.
- Follow TDD: write each behavior test, run it and observe the expected failure, then implement the smallest passing change.

## File Structure

### Backend repository

- Create `Ruumly.Backend/DTOs/Responses/ProviderCandidateDtos.cs`: typed candidate, location, anchor, and page contracts.
- Create `Ruumly.Backend/DTOs/Responses/OfferDeliveryDtos.cs`: typed outreach preview, public offer, and delivery preview contracts.
- Create `Ruumly.Backend/Helpers/ProviderCandidateFinder.cs`: materialization, deduplication, city anchor, Haversine distance, search, and ordering.
- Create `Ruumly.Backend/Helpers/ProviderOutreachComposer.cs`: provider language resolution and exact subject/body composition.
- Create `Ruumly.Backend/Helpers/OfferDeliveryComposer.cs`: public projection and exact customer email composition.
- Modify `Ruumly.Backend/Controllers/AdminLeadsController.cs`: expose provider candidates while preserving legacy matches.
- Modify `Ruumly.Backend/Controllers/AdminOffersController.cs`: outreach preview/dedup/resend, draft reuse/delete, delivery preview, and confirmation.
- Modify `Ruumly.Backend/Controllers/OffersController.cs`: shared projection and preference-only selection semantics.
- Modify `Ruumly.Backend/DTOs/Requests/OfferRequests.cs`: add `Resend` to outreach requests.
- Modify `Ruumly.Backend/Helpers/DemandLeadLifecycle.cs`: correct lifecycle documentation.
- Create `Ruumly.Backend.Tests/ProviderCandidateTests.cs`: geographic discovery contract tests.
- Modify `Ruumly.Backend.Tests/OfferLoopTests.cs`: outreach, draft, preview, selection, and confirmation tests.

### Frontend repository (`estonia-space-hub`)

- Create `src/components/offers/OfferPresentation.tsx`: reusable customer-facing offer content.
- Create `src/components/admin/leads/LeadWorkspace.tsx`: orchestration and query invalidation.
- Create `src/components/admin/leads/LeadProviderStage.tsx`: search, scope, selection, and outreach review.
- Create `src/components/admin/leads/LeadOfferStage.tsx`: outreach history, active draft, options, and deletion.
- Create `src/components/admin/leads/LeadDeliveryReview.tsx`: exact email/page preview, send effects, and booking confirmation.
- Create `src/components/admin/leads/LeadActivityTimeline.tsx`: compact lifecycle history.
- Create `src/components/admin/leads/leadWorkspaceModels.ts`: editable option conversion and shared stage types.
- Modify `src/components/admin/AdminLeads.tsx`: retain list/filter ownership and render the extracted workspace.
- Modify `src/pages/OfferPage.tsx`: use shared presentation and preference wording.
- Modify `src/i18n/LanguageContext.tsx`: expose language-specific translation lookup for admin previews.
- Modify `src/services/index.ts`: typed additive API clients.
- Modify `src/services/queryKeys.ts`: candidate and delivery-preview keys.
- Modify `src/i18n/translations.ts`: five-language operator and customer copy.
- Modify `e2e/fixtures.ts`: stateful mocks for all additive contracts.
- Modify `e2e/16-offer-page.spec.ts`: pending-preference semantics.
- Modify `e2e/17-admin-workspace.spec.ts`: guided workflow, deletion, preview, send, and mobile coverage.

---

### Task 1: Geographic provider candidate discovery

**Files:**
- Create: `Ruumly.Backend/DTOs/Responses/ProviderCandidateDtos.cs`
- Create: `Ruumly.Backend/Helpers/ProviderCandidateFinder.cs`
- Create: `Ruumly.Backend.Tests/ProviderCandidateTests.cs`
- Modify: `Ruumly.Backend/Controllers/AdminLeadsController.cs:240-350`

**Interfaces:**
- Produces: `ProviderCandidateFinder.SearchAsync(RuumlyDbContext, DemandLead, ProviderCandidateSearch, CancellationToken)`.
- Produces: `GET /api/admin/leads/{id}/provider-candidates?q=&scope=nearby|all&category=lead|any&radiusKm=25&limit=50`.
- Preserves: `GET /api/admin/leads/{id}/matches` unchanged for current consumers.

- [ ] **Step 1: Add failing geographic and validation tests**

Create the response contract expected by the tests:

```csharp
namespace Ruumly.Backend.DTOs.Responses;

public sealed record ProviderCandidateAnchorDto(double Lat, double Lng);

public sealed record ProviderCandidateLocationDto(
    Guid LocationId,
    string LocationName,
    string City,
    string Address,
    double? Lat,
    double? Lng,
    double? DistanceKm);

public sealed record ProviderCandidateDto(
    Guid SupplierId,
    string SupplierName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<string> ServiceTypes,
    Guid? LocationId,
    string? LocationName,
    string? City,
    string? Address,
    double? Lat,
    double? Lng,
    double? DistanceKm,
    bool IsExactCity,
    Guid? ListingId,
    string? ListingTitle,
    decimal? Price,
    string? PriceUnit,
    bool AlreadyContacted,
    DateTime? LastOutreachAt,
    IReadOnlyList<ProviderCandidateLocationDto> OtherLocations);

public sealed record ProviderCandidateResponse(
    IReadOnlyList<ProviderCandidateDto> Items,
    int Total,
    string Scope,
    double RadiusKm,
    ProviderCandidateAnchorDto? Anchor);
```

In `ProviderCandidateTests.cs`, seed one Tartu lead and seven active warehouse suppliers at these distances/cities: Tartu 0.6, 1.2, 1.5, and 3.8 km; Vahi 4.6 km; Tõrvandi 6.9 km; Reola 8.4 km. Give one supplier three matching listings to prove deduplication. Add these tests:

```csharp
[Fact]
public async Task Nearby_ReturnsSevenUniqueSuppliers_OrderedByDistance()
{
    var (db, lead, expectedIds) = await CandidateFixture.CreateTartuAsync();
    var controller = MakeAdminLeads(db);

    var result = await controller.GetProviderCandidates(
        lead.Id, q: null, scope: "nearby", category: "lead", radiusKm: 25, limit: 50);

    var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
    var items = ReadItems(body);
    items.Select(x => Read<Guid>(x, "supplierId")).Should().Equal(expectedIds);
    items.Select(x => Read<Guid>(x, "supplierId")).Should().OnlyHaveUniqueItems();
    items.Select(x => Read<double?>(x, "distanceKm")).Should().BeInAscendingOrder();
}

[Fact]
public async Task Search_AllEstonia_FindsNameCityAddressEmailAndPhone()
{
    var (db, lead, _) = await CandidateFixture.CreateTartuAsync();
    var controller = MakeAdminLeads(db);

    foreach (var q in new[] { "Panicom", "Tõrvandi", "Ringtee", "sales@", "+372" })
    {
        var result = await controller.GetProviderCandidates(
            lead.Id, q, "all", "lead", 25, 50);
        ReadItems(result.Should().BeOfType<OkObjectResult>().Subject.Value!)
            .Should().NotBeEmpty(q);
    }
}

[Fact]
public async Task AnyCategory_IsRejectedForNearby_AndAllowedForAll()
{
    var (db, lead, _) = await CandidateFixture.CreateTartuAsync();
    var controller = MakeAdminLeads(db);

    (await controller.GetProviderCandidates(lead.Id, null, "nearby", "any", 25, 50))
        .Should().BeOfType<BadRequestObjectResult>();
    (await controller.GetProviderCandidates(lead.Id, null, "all", "any", 25, 50))
        .Should().BeOfType<OkObjectResult>();
}

[Fact]
public async Task MissingCoordinates_FallsBackToExactCityThenName()
{
    var (db, lead) = await CandidateFixture.CreateWithoutCoordinatesAsync();
    var result = await MakeAdminLeads(db).GetProviderCandidates(
        lead.Id, null, "nearby", "lead", 25, 50);
    var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
    Read<object?>(body, "anchor").Should().BeNull();
    ReadItems(body).Select(x => Read<string>(x, "city")).First().Should().Be("Tartu");
}
```

- [ ] **Step 2: Run the targeted tests and verify RED**

Run from `Ruumly.Backend/`:

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter FullyQualifiedName~ProviderCandidateTests
```

Expected: build failure because `GetProviderCandidates`, `ProviderCandidateSearch`, and `ProviderCandidateFinder` do not exist.

- [ ] **Step 3: Implement candidate materialization, deduplication, and distance ordering**

Define the search input in `ProviderCandidateFinder.cs`:

```csharp
public sealed record ProviderCandidateSearch(
    string? Query,
    bool AllEstonia,
    bool AllCategories,
    double RadiusKm,
    int Limit);

public static class ProviderCandidateFinder
{
    private const double EarthRadiusKm = 6371.0088;

    public static async Task<ProviderCandidateResponse> SearchAsync(
        RuumlyDbContext db,
        DemandLead lead,
        ProviderCandidateSearch search,
        CancellationToken ct = default)
    {
        var suppliers = await db.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Include(s => s.Listings.Where(l => l.IsActive))
            .ToListAsync(ct);

        var supplierIds = suppliers.Select(s => s.Id).ToList();
        var locations = await db.SupplierLocations
            .AsNoTracking()
            .Where(l => l.IsActive && supplierIds.Contains(l.SupplierId))
            .ToListAsync(ct);

        var contacted = await db.ProviderOutreaches
            .AsNoTracking()
            .Where(o => o.DemandLeadId == lead.Id)
            .GroupBy(o => o.SupplierId)
            .Select(g => new { SupplierId = g.Key, Last = g.Max(x => x.SentAt) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Last, ct);

        return BuildResponse(suppliers, locations, contacted, lead, search);
    }

    internal static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double value) => value * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2))
              * Math.Pow(Math.Sin(dLng / 2), 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
```

`BuildResponse` must perform these operations in this order:

1. Filter by active matching listing or active directory supplier service JSON, unless `AllCategories` is true.
2. Deduplicate by `Supplier.Id` before applying `Limit`.
3. Build active location rows; when none exist, build one fallback row from the supplier's best matching active listing.
4. Derive the city anchor from the average of valid exact-city coordinates across all active locations/listings; reject out-of-range values and `(0,0)` as missing data.
5. Choose each supplier's closest location, calculate distance rounded to one decimal, and retain remaining locations in `OtherLocations`.
6. Apply case-insensitive search over supplier name, location name, city, address, email, and phone.
7. If `AllEstonia` is false and an anchor exists, retain rows with `DistanceKm <= RadiusKm`; if no anchor exists, retain exact-city rows and query matches without applying an impossible radius.
8. Sort exact city descending, distance ascending with null last, then supplier name ordinal-ignore-case.
9. Apply `Take(Limit)` only after all filtering and deduplication; set `Total` before the limit.

Add the endpoint with exact parsing:

```csharp
[HttpGet("leads/{id:guid}/provider-candidates")]
public async Task<IActionResult> GetProviderCandidates(
    Guid id,
    [FromQuery] string? q = null,
    [FromQuery] string scope = "nearby",
    [FromQuery] string category = "lead",
    [FromQuery] double radiusKm = 25,
    [FromQuery] int limit = 50,
    CancellationToken ct = default)
{
    var lead = await Db.DemandLeads.FindAsync([id], ct);
    if (lead is null) return NotFound(Error("Lead not found."));

    var allEstonia = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
    if (!allEstonia && !string.Equals(scope, "nearby", StringComparison.OrdinalIgnoreCase))
        return BadRequest(Error("scope must be nearby or all."));

    var allCategories = string.Equals(category, "any", StringComparison.OrdinalIgnoreCase);
    if (!allCategories && !string.Equals(category, "lead", StringComparison.OrdinalIgnoreCase))
        return BadRequest(Error("category must be lead or any."));
    if (allCategories && !allEstonia)
        return BadRequest(Error("category=any requires scope=all."));

    var search = new ProviderCandidateSearch(
        q?.Trim(), allEstonia, allCategories,
        Math.Clamp(radiusKm, 1, 250), Math.Clamp(limit, 1, 200));
    return Ok(await ProviderCandidateFinder.SearchAsync(Db, lead, search, ct));
}
```

- [ ] **Step 4: Run candidate tests and the existing concierge lead tests**

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter "FullyQualifiedName~ProviderCandidateTests|FullyQualifiedName~ConciergeLeadTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit**

```powershell
git add Ruumly.Backend/DTOs/Responses/ProviderCandidateDtos.cs Ruumly.Backend/Helpers/ProviderCandidateFinder.cs Ruumly.Backend/Controllers/AdminLeadsController.cs Ruumly.Backend.Tests/ProviderCandidateTests.cs
git commit -m "feat: add geographic provider candidate search"
```

### Task 2: Provider outreach preview, deduplication, and explicit resend

**Files:**
- Create: `Ruumly.Backend/Helpers/ProviderOutreachComposer.cs`
- Create: `Ruumly.Backend/DTOs/Responses/OfferDeliveryDtos.cs`
- Modify: `Ruumly.Backend/DTOs/Requests/OfferRequests.cs:37-40`
- Modify: `Ruumly.Backend/Controllers/AdminOffersController.cs:203-294`
- Modify: `Ruumly.Backend.Tests/OfferLoopTests.cs:439-520`

**Interfaces:**
- Produces: `POST /api/admin/leads/{id}/outreach/preview` with `{ supplierIds }`.
- Extends: `POST /api/admin/leads/{id}/outreach` with `{ supplierIds, resend }`.
- Produces: `ProviderOutreachComposer.Compose(DemandLead, Supplier)` used by both paths.

- [ ] **Step 1: Write failing preview and duplicate-delivery tests**

Add:

```csharp
[Fact]
public async Task PreviewOutreach_IsSideEffectFree_AndMatchesDeliveredMessageByteForByte()
{
    var db = TestDbContext.Create();
    var queue = new CapturingEmailQueue();
    var lead = MakeLead(db);
    var supplier = MakeSupplier(db, "HasEmail OÜ", "provider@x.ee");
    var admin = MakeAdmin(db, queue);

    var previewResult = await admin.PreviewOutreach(
        lead.Id, new OutreachPreviewRequest([supplier.Id]));
    var preview = FirstPreview(previewResult.Should().BeOfType<OkObjectResult>().Subject.Value!);

    db.ProviderOutreaches.Should().BeEmpty();
    queue.Emails.Should().BeEmpty();

    await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
    var delivered = queue.Emails.Should().ContainSingle().Subject;
    Read<string>(preview, "subject").Should().Be(delivered.Subject);
    Read<string>(preview, "textBody").Should().Be(delivered.TextBody);
}

[Fact]
public async Task SendOutreach_DuplicateSkipsUnlessResendIsExplicit()
{
    var db = TestDbContext.Create();
    var queue = new CapturingEmailQueue();
    var lead = MakeLead(db);
    var supplier = MakeSupplier(db, "HasEmail OÜ", "provider@x.ee");
    var admin = MakeAdmin(db, queue);

    await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
    var duplicate = await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id]));
    ReadSkippedReason(duplicate).Should().Be("already_contacted");
    db.ProviderOutreaches.Should().ContainSingle();
    queue.Emails.Should().ContainSingle();

    await admin.SendOutreach(lead.Id, new OutreachRequest([supplier.Id], Resend: true));
    db.ProviderOutreaches.Should().HaveCount(2);
    queue.Emails.Should().HaveCount(2);
}
```

- [ ] **Step 2: Run the two tests and verify RED**

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter "PreviewOutreach_IsSideEffectFree|SendOutreach_DuplicateSkips"
```

Expected: build failure because preview DTOs, endpoint, composer, and `Resend` do not exist.

- [ ] **Step 3: Add typed preview contracts and one exact composer**

```csharp
public record OutreachPreviewRequest(List<Guid> SupplierIds);
public record OutreachRequest(List<Guid> SupplierIds, bool Resend = false);

public sealed record OutreachPreviewItemDto(
    Guid SupplierId,
    string? SupplierName,
    string? Email,
    string? Language,
    string? Subject,
    string? TextBody,
    string? SkipReason);

public sealed record OutreachPreviewResponse(IReadOnlyList<OutreachPreviewItemDto> Recipients);

public sealed record ProviderOutreachMessage(
    string Language, string Subject, string TextBody);
```

Implement the composer without controller-owned text:

```csharp
public static class ProviderOutreachComposer
{
    public static ProviderOutreachMessage Compose(DemandLead lead, Supplier supplier)
    {
        var language = supplier.Country?.ToUpperInvariant() switch
        {
            "LV" => "lv",
            "LT" => "lt",
            "EE" => "et",
            _ => "en",
        };
        var t = EmailTranslations.For(language);
        var route = string.IsNullOrWhiteSpace(lead.ToCity)
            ? lead.City
            : $"{lead.City} → {lead.ToCity}";
        var date = lead.NeedDate?.ToString("yyyy-MM-dd") ?? "—";
        var details = string.IsNullOrWhiteSpace(lead.Details) ? "—" : lead.Details;
        var category = t.CategoryLabel(lead.Category);
        var body = $"{t.OutreachGreeting}\n\n"
                 + $"{t.OutreachBody(category, route, details, date)}\n\n"
                 + $"{t.OutreachAsk}\n\n{t.OutreachSignature}";
        return new(language, t.OutreachSubject(category, route), body);
    }
}
```

Preview must return one row per requested supplier, including `not_found`, `no_email`, or `already_contacted` in `SkipReason`; it must still include composed text for an already-contacted supplier so the explicit resend confirmation can display it. Send must query existing `(DemandLeadId, SupplierId)` pairs once, skip them when `Resend` is false, save all new rows before queueing any email, and use the composer output unchanged.

For relational providers, wrap duplicate-check plus row creation in a serializable transaction. Queue email only after commit. A serialization failure returns a retryable 409 and never queues email; the next operator retry observes the saved outreach and returns `already_contacted`. InMemory tests run the same branch without a transaction.

- [ ] **Step 4: Run offer-loop tests**

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter FullyQualifiedName~OfferLoopTests
```

Expected: all `OfferLoopTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add Ruumly.Backend/Helpers/ProviderOutreachComposer.cs Ruumly.Backend/DTOs/Responses/OfferDeliveryDtos.cs Ruumly.Backend/DTOs/Requests/OfferRequests.cs Ruumly.Backend/Controllers/AdminOffersController.cs Ruumly.Backend.Tests/OfferLoopTests.cs
git commit -m "feat: preview and deduplicate provider outreach"
```

### Task 3: Draft reuse, deletion, and exact delivery preview

**Files:**
- Create: `Ruumly.Backend/Helpers/OfferDeliveryComposer.cs`
- Modify: `Ruumly.Backend/DTOs/Responses/OfferDeliveryDtos.cs`
- Modify: `Ruumly.Backend/Controllers/AdminOffersController.cs:30-200`
- Modify: `Ruumly.Backend/Controllers/OffersController.cs:30-145`
- Modify: `Ruumly.Backend.Tests/OfferLoopTests.cs:120-340`

**Interfaces:**
- Produces: `DELETE /api/admin/offers/{id}` for Draft only.
- Produces: `GET /api/admin/offers/{id}/delivery-preview`.
- Produces: `OfferDeliveryComposer.ToPublic(Offer)` and `OfferDeliveryComposer.ComposeEmail(Offer, string)`.

- [ ] **Step 1: Write failing lifecycle and preview tests**

```csharp
[Fact]
public async Task CreateOffer_ReusesNewestExistingDraft()
{
    var db = TestDbContext.Create();
    var lead = MakeLead(db);
    var admin = MakeAdmin(db, new CapturingEmailQueue());
    var first = await admin.CreateOffer(lead.Id, new CreateOfferRequest());
    var second = await admin.CreateOffer(lead.Id, new CreateOfferRequest());
    Read<Guid>(Value(first), "id").Should().Be(Read<Guid>(Value(second), "id"));
    db.Offers.Should().ContainSingle();
}

[Fact]
public async Task DeleteOffer_DeletesDraftAndOptions_ButRejectsSent()
{
    var db = TestDbContext.Create();
    var lead = MakeLead(db);
    var admin = MakeAdmin(db, new CapturingEmailQueue());
    var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
        Options: [new OfferOptionInput("Option") ]));
    var id = Read<Guid>(Value(created), "id");
    (await admin.DeleteOffer(id)).Should().BeOfType<NoContentResult>();
    db.Offers.Should().BeEmpty();
    db.OfferOptions.Should().BeEmpty();

    var sent = await MakeSentOffer(db, lead);
    (await admin.DeleteOffer(sent.Id)).Should().BeOfType<ConflictObjectResult>();
}

[Fact]
public async Task DeliveryPreview_IsSideEffectFree_AndMatchesSendEmailAndPublicProjection()
{
    var db = TestDbContext.Create();
    var queue = new CapturingEmailQueue();
    var lead = MakeLead(db);
    var admin = MakeAdmin(db, queue);
    var created = await admin.CreateOffer(lead.Id, new CreateOfferRequest(
        CustomerNote: "Call first", Options: [new OfferOptionInput("Option", PriceAmount: 89)]));
    var id = Read<Guid>(Value(created), "id");

    var preview = Value(await admin.GetDeliveryPreview(id));
    db.Offers.Single().ViewedAt.Should().BeNull();
    db.Offers.Single().Status.Should().Be(OfferStatus.Draft);

    await admin.SendOffer(id);
    var email = queue.Emails.Should().ContainSingle().Subject;
    Read<string>(Read<object>(preview, "email"), "subject").Should().Be(email.Subject);
    Read<string>(Read<object>(preview, "email"), "textBody").Should().Be(email.TextBody);

    var publicDto = Value(await MakePublic(db, new CapturingEmailQueue())
        .GetOffer(db.Offers.Single().Token));
    var previewPage = Read<object>(preview, "page");
    Read<object>(previewPage, "lead").Should().BeEquivalentTo(Read<object>(publicDto, "lead"));
    Read<object>(previewPage, "options").Should().BeEquivalentTo(Read<object>(publicDto, "options"));
    Read<string?>(previewPage, "customerNote").Should().Be(Read<string?>(publicDto, "customerNote"));
}
```

- [ ] **Step 2: Run targeted tests and verify RED**

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter "CreateOffer_Reuses|DeleteOffer_Deletes|DeliveryPreview_IsSideEffectFree"
```

Expected: failures because reuse, delete, preview, and shared projection do not exist.

- [ ] **Step 3: Add typed public and preview DTOs**

```csharp
public sealed record PublicOfferLeadDto(
    string Category, string City, string? ToCity, DateTime? NeedDate, string? Details);

public sealed record PublicOfferOptionDto(
    Guid Id, string Title, decimal? PriceAmount, string? PriceUnit,
    string? Notes, string? SupplierName);

public sealed record PublicOfferDto(
    string Status, string Language, string? CustomerNote, DateTime? SentAt,
    Guid? ChosenOptionId, PublicOfferLeadDto? Lead,
    IReadOnlyList<PublicOfferOptionDto> Options);

public sealed record OfferDeliveryRecipientDto(string? Name, string Email);
public sealed record OfferDeliveryEmailDto(string Subject, string TextBody);
public sealed record OfferDeliveryPreviewDto(
    OfferDeliveryRecipientDto Recipient,
    OfferDeliveryEmailDto Email,
    PublicOfferDto Page);
public sealed record OfferEmailMessage(string Subject, string TextBody, string Link);
```

Implement `OfferDeliveryComposer` so `ToPublic` contains only the fields above and `ComposeEmail` reproduces the existing ordered option, price, note, CTA, questions, and signature text exactly. It receives the already-localized public link, preventing URL construction from diverging between preview and send.

- [ ] **Step 4: Implement draft reuse, deletion, preview, and shared send/public paths**

At the start of `CreateOffer`, load the newest Draft including options and suppliers; return `MapOffer(existingDraft)` without mutating it. Add:

For relational providers, perform the Draft lookup and conditional creation in a serializable transaction scoped to the lead. The request body is ignored when a Draft already exists; callers PATCH the returned Draft explicitly. This prevents silent option merging and makes retries deterministic.

```csharp
[HttpDelete("offers/{id:guid}")]
public async Task<IActionResult> DeleteOffer(Guid id)
{
    var offer = await Db.Offers.Include(o => o.Options).FirstOrDefaultAsync(o => o.Id == id);
    if (offer is null) return NotFound(Error("Offer not found."));
    if (offer.Status != OfferStatus.Draft)
        return Conflict(Error("Only draft offers can be deleted."));
    Db.Offers.Remove(offer);
    Audit("offer.deleted", User.GetUserId().ToString(), id.ToString(), $"Lead: {offer.DemandLeadId}");
    await Db.SaveChangesAsync();
    return NoContent();
}

[HttpGet("offers/{id:guid}/delivery-preview")]
public async Task<IActionResult> GetDeliveryPreview(Guid id)
{
    var offer = await LoadOfferForDelivery(id);
    if (offer is null) return NotFound(Error("Offer not found."));
    if (string.IsNullOrWhiteSpace(offer.DemandLead?.Email))
        return BadRequest(Error("The lead has no email address."));
    if (offer.Options.Count == 0)
        return BadRequest(Error("Add at least one option before previewing."));
    var link = FrontendUrl.Localized(config["AppUrl"], offer.Language, $"offer/{offer.Token}");
    var email = OfferDeliveryComposer.ComposeEmail(offer, link);
    return Ok(new OfferDeliveryPreviewDto(
        new(offer.DemandLead.Name, offer.DemandLead.Email.Trim()),
        new(email.Subject, email.TextBody),
        OfferDeliveryComposer.ToPublic(offer)));
}
```

`SendOffer` must call the same `ComposeEmail`. `OffersController.GetOffer` must call `OfferDeliveryComposer.ToPublic` after applying its real first-view transition. Remove the old controller-local `BuildOfferEmailBody`, `FormatPrice`, and `MapPublic` methods.

- [ ] **Step 5: Run all offer-loop tests**

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter FullyQualifiedName~OfferLoopTests
```

Expected: all tests pass, including existing PII-redaction tests.

- [ ] **Step 6: Commit**

```powershell
git add Ruumly.Backend/Helpers/OfferDeliveryComposer.cs Ruumly.Backend/DTOs/Responses/OfferDeliveryDtos.cs Ruumly.Backend/Controllers/AdminOffersController.cs Ruumly.Backend/Controllers/OffersController.cs Ruumly.Backend.Tests/OfferLoopTests.cs
git commit -m "feat: add safe offer draft and delivery preview flow"
```

### Task 4: Preference-only customer selection and admin booking confirmation

**Files:**
- Modify: `Ruumly.Backend/Controllers/OffersController.cs:55-115`
- Modify: `Ruumly.Backend/Controllers/AdminOffersController.cs`
- Modify: `Ruumly.Backend/Controllers/AdminLeadsController.cs:160-220`
- Modify: `Ruumly.Backend/Helpers/DemandLeadLifecycle.cs:5-12`
- Modify: `Ruumly.Backend.Tests/OfferLoopTests.cs:340-425`

**Interfaces:**
- Changes: `POST /api/offers/{token}/choose` leaves the lead `Quoted`.
- Produces: `POST /api/admin/offers/{id}/confirm-booking`, idempotently returning the mapped offer.

- [ ] **Step 1: Replace the old conversion test with failing preference and confirmation tests**

```csharp
[Fact]
public async Task ChooseOption_LeavesLeadQuotedUntilAdminConfirms()
{
    var db = TestDbContext.Create();
    var lead = MakeLead(db);
    var offer = await MakeSentOffer(db, lead);
    await MakePublic(db, new CapturingEmailQueue())
        .ChooseOption(offer.Token, new ChooseOptionRequest(offer.Options[0].Id));
    db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Quoted);
}

[Fact]
public async Task ConfirmBooking_RequiresChosenOffer_ConvertsLead_AndIsIdempotent()
{
    var db = TestDbContext.Create();
    var lead = MakeLead(db);
    var offer = await MakeSentOffer(db, lead);
    var admin = MakeAdmin(db, new CapturingEmailQueue());
    (await admin.ConfirmBooking(offer.Id)).Should().BeOfType<ConflictObjectResult>();

    await MakePublic(db, new CapturingEmailQueue())
        .ChooseOption(offer.Token, new ChooseOptionRequest(offer.Options[0].Id));
    (await admin.ConfirmBooking(offer.Id)).Should().BeOfType<OkObjectResult>();
    db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Converted);
    db.Bookings.Should().BeEmpty();
    db.Orders.Should().BeEmpty();

    (await admin.ConfirmBooking(offer.Id)).Should().BeOfType<OkObjectResult>();
    db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.Converted);
}

[Fact]
public async Task ManualLeadPatch_CannotBypassBookingConfirmation()
{
    var db = TestDbContext.Create();
    var lead = MakeLead(db);
    var result = await MakeAdminLeads(db)
        .UpdateLead(lead.Id, new UpdateLeadRequest(Status: "converted"));
    result.Should().BeOfType<ConflictObjectResult>();
    db.DemandLeads.Single().Status.Should().Be(DemandLeadStatus.New);
}
```

- [ ] **Step 2: Run and verify the semantic failure**

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter "ChooseOption_LeavesLeadQuoted|ConfirmBooking_RequiresChosen"
```

Expected: first test gets `Converted`; second fails because `ConfirmBooking` does not exist.

- [ ] **Step 3: Remove conversion from public choose and add admin confirmation**

Delete the `DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Converted)` block from `ChooseOption`. Add:

```csharp
[HttpPost("offers/{id:guid}/confirm-booking")]
public async Task<IActionResult> ConfirmBooking(Guid id)
{
    var offer = await Db.Offers
        .Include(o => o.Options).ThenInclude(op => op.Supplier)
        .Include(o => o.DemandLead)
        .FirstOrDefaultAsync(o => o.Id == id);
    if (offer is null) return NotFound(Error("Offer not found."));
    if (offer.Status != OfferStatus.Chosen || offer.ChosenOptionId is null)
        return Conflict(Error("The customer has not requested an option."));
    if (offer.Options.All(o => o.Id != offer.ChosenOptionId.Value))
        return Conflict(Error("The requested option no longer exists."));

    var lead = offer.DemandLead!;
    if (lead.Status != DemandLeadStatus.Converted)
    {
        DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Converted);
        Audit("offer.booking_confirmed", User.GetUserId().ToString(), offer.Id.ToString(),
              $"Lead: {lead.Id}, option: {offer.ChosenOptionId}");
        await Db.SaveChangesAsync();
    }
    return Ok(MapOffer(offer));
}
```

Update the `DemandLeadLifecycle` summary to say `send offer -> Quoted, send outreach -> Contacted, admin confirmation -> Converted`.

In `AdminLeadsController.UpdateLead`, reject a requested `Converted` status with HTTP 409 and `Confirm the customer's chosen offer instead.` Existing Converted leads remain readable/filterable, but neither the row status menu nor the generic PATCH endpoint can fabricate a booking outcome.

- [ ] **Step 4: Run backend offer and metrics tests**

```powershell
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj --filter "FullyQualifiedName~OfferLoopTests|FullyQualifiedName~ConciergeLeadTests"
```

Expected: all selected tests pass; the quote-to-booking metric counts only the admin-confirmed Converted lead.

- [ ] **Step 5: Commit**

```powershell
git add Ruumly.Backend/Controllers/OffersController.cs Ruumly.Backend/Controllers/AdminOffersController.cs Ruumly.Backend/Helpers/DemandLeadLifecycle.cs Ruumly.Backend.Tests/OfferLoopTests.cs
git commit -m "fix: confirm concierge bookings after provider approval"
```

### Task 5: Shared customer offer presentation and pending-request wording

**Files:**
- Create: `estonia-space-hub/src/components/offers/OfferPresentation.tsx`
- Modify: `estonia-space-hub/src/pages/OfferPage.tsx`
- Modify: `estonia-space-hub/src/i18n/LanguageContext.tsx`
- Modify: `estonia-space-hub/src/i18n/translations.ts`
- Modify: `estonia-space-hub/e2e/16-offer-page.spec.ts`

**Interfaces:**
- Produces: `OfferPresentation` used by the public page and Task 8 admin preview.
- Consumes: existing `PublicOffer` and `PublicOfferOption` service types.

- [ ] **Step 1: Update E2E assertions first**

Change the happy path to assert:

```typescript
await page.getByRole("button", { name: /küsi seda pakkumist/i }).first().click();
await expect(page.getByText(/see ei ole veel kinnitatud broneering/i)).toBeVisible();
await page.getByRole("button", { name: /jah, saada soov/i }).click();
await expect(page.getByText(/soov saadetud/i)).toBeVisible();
await expect(page.getByText(/ruumly kinnitab saadavuse partneriga/i)).toBeVisible();
```

Update the already-chosen test to expect a pending-confirmation state and no request buttons.

- [ ] **Step 2: Run the offer page test and verify RED**

```powershell
npx playwright test e2e/16-offer-page.spec.ts
```

Expected: old `Vali see pakkumine` and `Valik kinnitatud` wording causes failures.

- [ ] **Step 3: Extract a pure reusable presentation component**

Use this public interface:

```tsx
export interface OfferPresentationProps {
  offer: PublicOffer;
  action?: {
    label: string;
    pendingOptionId: string | null;
    disabled: boolean;
    onRequest(option: PublicOfferOption): void;
  };
  preview?: boolean;
}

export function OfferPresentation({ offer, action, preview = false }: OfferPresentationProps) {
  const t = (key: string) => translateForLanguage(offer.language, key);
  const chosen = offer.status === "chosen";
  return (
    <div data-testid={preview ? "offer-preview" : "offer-presentation"} className="space-y-5">
      {offer.customerNote && <p className="rounded-md border bg-muted/40 p-4 text-sm">{offer.customerNote}</p>}
      <div className="grid gap-4 md:grid-cols-2">
        {offer.options.map((option) => (
          <article key={option.id} className="rounded-md border bg-card p-4 shadow-sm">
            <div className="flex items-start justify-between gap-3">
              <div>
                <h2 className="text-base font-semibold">{option.title}</h2>
                {option.supplierName && <p className="mt-1 text-sm text-muted-foreground">{option.supplierName}</p>}
              </div>
              {offer.chosenOptionId === option.id && <span className="text-xs font-medium text-success">{t("offer.yourRequest")}</span>}
            </div>
            {option.priceAmount != null && <p className="mt-3 text-xl font-semibold">€{option.priceAmount}{option.priceUnit ? ` / ${option.priceUnit}` : ""}</p>}
            {option.notes && <p className="mt-2 text-sm text-muted-foreground">{option.notes}</p>}
            {!chosen && action && (
              <Button className="mt-4 min-h-11 w-full" disabled={action.disabled} onClick={() => action.onRequest(option)}>
                {action.pendingOptionId === option.id ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
                {action.label}
              </Button>
            )}
          </article>
        ))}
      </div>
    </div>
  );
}
```

Keep the page header, invalid/error states, query, mutation, and alert dialog in `OfferPage`. The dialog and success banner must explicitly say the request is pending provider confirmation and no payment has occurred.

Export this helper from `LanguageContext.tsx` and use it in `OfferPresentation`, so an Estonian admin previewing a Russian offer sees Russian customer-facing text:

```typescript
export function translateForLanguage(language: string, key: string): string {
  const normalized: Language = isSupportedLang(language) ? language : DEFAULT_LANG;
  return translations[normalized]?.[key] || translations.et[key] || key;
}
```

- [ ] **Step 4: Add exact five-language customer copy**

Add these keys to all language blocks:

| Key | et | en | ru | lv | lt |
|---|---|---|---|---|---|
| `offer.requestThis` | Küsi seda pakkumist | Request this offer | Запросить это предложение | Pieprasīt šo piedāvājumu | Užklausti šį pasiūlymą |
| `offer.requestConfirmTitle` | Saada see soov Ruumlyle? | Send this request to Ruumly? | Отправить запрос в Ruumly? | Nosūtīt šo pieprasījumu Ruumly? | Siųsti šią užklausą Ruumly? |
| `offer.requestConfirmBody` | See ei ole veel kinnitatud broneering. Ruumly kontrollib saadavuse partneriga; makset ei tehta. | This is not a confirmed booking yet. Ruumly will check availability with the provider; no payment is taken. | Это ещё не подтверждённое бронирование. Ruumly проверит доступность у партнёра; оплата не взимается. | Šī vēl nav apstiprināta rezervācija. Ruumly pārbaudīs pieejamību pie partnera; maksājums netiks veikts. | Tai dar nėra patvirtintas užsakymas. Ruumly patikrins prieinamumą su partneriu; mokėjimas nebus nuskaičiuotas. |
| `offer.requestConfirmAction` | Jah, saada soov | Yes, send request | Да, отправить запрос | Jā, nosūtīt pieprasījumu | Taip, siųsti užklausą |
| `offer.requestSent` | Soov saadetud | Request sent | Запрос отправлен | Pieprasījums nosūtīts | Užklausa išsiųsta |
| `offer.requestSentBody` | Ruumly kinnitab saadavuse partneriga ja võtab sinuga ühendust. | Ruumly will confirm availability with the provider and contact you. | Ruumly подтвердит доступность у партнёра и свяжется с вами. | Ruumly apstiprinās pieejamību pie partnera un sazināsies ar jums. | Ruumly patvirtins prieinamumą su partneriu ir susisieks su jumis. |
| `offer.yourRequest` | Sinu soov | Your request | Ваш запрос | Tavs pieprasījums | Jūsų užklausa |

- [ ] **Step 5: Run public offer E2E, translations, and TypeScript**

```powershell
npx playwright test e2e/16-offer-page.spec.ts
npm test -- src/test/translations.test.ts
npx tsc --noEmit
```

Expected: all commands pass.

- [ ] **Step 6: Commit in the frontend repository**

```powershell
git add src/components/offers/OfferPresentation.tsx src/pages/OfferPage.tsx src/i18n/translations.ts e2e/16-offer-page.spec.ts
git commit -m "fix: present customer offer choice as a pending request"
```

### Task 6: Guided provider discovery and outreach review stage

**Files:**
- Create: `estonia-space-hub/src/components/admin/leads/LeadProviderStage.tsx`
- Modify: `estonia-space-hub/src/services/index.ts:570-750`
- Modify: `estonia-space-hub/src/services/queryKeys.ts:145-154`
- Modify: `estonia-space-hub/src/components/admin/AdminLeads.tsx:180-790`
- Modify: `estonia-space-hub/src/i18n/translations.ts`
- Modify: `estonia-space-hub/e2e/fixtures.ts:275-430`
- Modify: `estonia-space-hub/e2e/17-admin-workspace.spec.ts`

**Interfaces:**
- Consumes Task 1 candidate response and Task 2 outreach preview/send response.
- Produces `LeadProviderStage({ lead, onAddCandidate })`.

- [ ] **Step 1: Add failing provider discovery E2E coverage**

Add a fixture with the seven named Tartu-area providers, including distances 0.6 through 8.4 km, one provider without email, and one already contacted provider. Assert:

```typescript
await openWorkspace(page);
await expect(page.getByText("Rare Minilaod")).toBeVisible();
await expect(page.getByText("Kapsel Minilaod")).toBeVisible();
await expect(page.getByText("8.4 km")).toBeVisible();
await page.getByPlaceholder(/search providers/i).fill("Panicom");
await expect(page.getByText("Panicom Miniladu")).toBeVisible();
await expect(page.getByText("Rare Minilaod")).toHaveCount(0);
await page.getByRole("button", { name: /all estonia/i }).click();
await page.getByRole("button", { name: /all services/i }).click();
await page.getByRole("checkbox", { name: "Panicom Miniladu" }).check();
await page.getByRole("button", { name: /review message to 1 provider/i }).click();
await expect(page.getByRole("dialog")).toContainText("sales@panicom.ee");
await expect(page.getByRole("dialog")).toContainText("Ruumly");
```

- [ ] **Step 2: Run the admin workspace test and verify RED**

```powershell
npx playwright test e2e/17-admin-workspace.spec.ts --grep "nearby provider discovery"
```

Expected: candidate endpoint and guided stage controls do not exist.

- [ ] **Step 3: Add typed API clients and query keys**

```typescript
export interface ProviderCandidateLocation {
  locationId: string; locationName: string; city: string; address: string;
  lat: number | null; lng: number | null; distanceKm: number | null;
}
export interface ProviderCandidate {
  supplierId: string; supplierName: string; contactEmail: string | null;
  contactPhone: string | null; serviceTypes: string[]; locationId: string | null;
  locationName: string | null; city: string | null; address: string | null;
  lat: number | null; lng: number | null; distanceKm: number | null;
  isExactCity: boolean; listingId: string | null; listingTitle: string | null;
  price: number | null; priceUnit: string | null; alreadyContacted: boolean;
  lastOutreachAt: string | null; otherLocations: ProviderCandidateLocation[];
}
export interface ProviderCandidateResponse {
  items: ProviderCandidate[]; total: number; scope: "nearby" | "all";
  radiusKm: number; anchor: { lat: number; lng: number } | null;
}
export interface OutreachPreviewItem {
  supplierId: string; supplierName: string | null; email: string | null;
  language: string | null; subject: string | null; textBody: string | null;
  skipReason: "not_found" | "no_email" | "already_contacted" | null;
}
```

Add service methods `adminLeadService.candidates(id, opts)`, `adminOfferService.previewOutreach(leadId, supplierIds)`, and extend `outreach(leadId, supplierIds, resend=false)`. Candidate query keys include every filter value; preview is a mutation and is not cached.

- [ ] **Step 4: Build `LeadProviderStage` and replace the old match panel**

The component owns `q`, `scope`, `category`, `radiusKm`, selected supplier IDs, expanded locations, preview state, and explicit resend confirmation. Its prop contract is:

```typescript
interface LeadProviderStageProps {
  lead: AdminLead;
  outreachRows: ProviderOutreachRow[];
  onAddCandidate(candidate: ProviderCandidate): void;
  onOutreachComplete(): void;
}
```

Use a 300 ms debounced search. Render unique supplier rows with closest location, distance or `Distance unavailable`, service chips, contact actions, and expandable other locations. Email-less providers stay visible but their checkbox is disabled. The main button is full-width on mobile and opens a review dialog showing exact recipient, subject, and preformatted text body. Only that dialog's confirmation calls send.

Candidate-query failure renders the existing generic error plus a `Retry` button and must not render the empty-state message. Preview failure keeps the current selection and dialog trigger available. Send failure keeps the review dialog open; skipped `no_email`, `not_found`, and `already_contacted` rows remain visible with their exact reason instead of being counted as sent.

- [ ] **Step 5: Add exact five-language stage copy**

Add the provider-stage keys with the exact five-language values in Appendix A.

- [ ] **Step 6: Run targeted E2E, translation test, and TypeScript**

```powershell
npx playwright test e2e/17-admin-workspace.spec.ts --grep "provider|outreach"
npm test -- src/test/translations.test.ts
npx tsc --noEmit
```

Expected: all commands pass.

- [ ] **Step 7: Commit in the frontend repository**

```powershell
git add src/components/admin/leads/LeadProviderStage.tsx src/components/admin/AdminLeads.tsx src/services/index.ts src/services/queryKeys.ts src/i18n/translations.ts e2e/fixtures.ts e2e/17-admin-workspace.spec.ts
git commit -m "feat: guide provider discovery and outreach review"
```

### Task 7: Active offer draft, option editor, history, and deletion

**Files:**
- Create: `estonia-space-hub/src/components/admin/leads/leadWorkspaceModels.ts`
- Create: `estonia-space-hub/src/components/admin/leads/LeadOfferStage.tsx`
- Modify: `estonia-space-hub/src/components/admin/AdminLeads.tsx`
- Modify: `estonia-space-hub/src/services/index.ts`
- Modify: `estonia-space-hub/src/i18n/translations.ts`
- Modify: `estonia-space-hub/e2e/fixtures.ts`
- Modify: `estonia-space-hub/e2e/17-admin-workspace.spec.ts`

**Interfaces:**
- Consumes Task 3 create-reuse and delete endpoints.
- Produces: `LeadOfferStage` with one active Draft and compact immutable history.
- Produces: `EditableOption`, `toEditable`, `candidateToEditable`, `toInput`, and `parsePrice` from `leadWorkspaceModels.ts`.

- [ ] **Step 1: Replace the contradictory draft-link test with failing draft workflow tests**

Delete the test named `draft offer keeps a real 'open page' link`. Add:

```typescript
test("draft is reused, has no public link, and can be deleted", async ({ page }) => {
  await openWorkspace(page);
  await page.getByRole("button", { name: /create offer/i }).click();
  await page.getByRole("button", { name: /create offer/i }).click();
  await expect(page.getByText(/one active draft/i)).toBeVisible();
  await expect(page.getByRole("link", { name: /open offer page/i })).toHaveCount(0);
  await page.getByRole("button", { name: /delete draft/i }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: /delete draft/i }).click();
  await expect(page.getByText(/draft deleted/i)).toBeVisible();
  await expect(page.getByRole("button", { name: /create offer/i })).toBeVisible();
});

test("sent offers remain in compact history while a new draft is edited", async ({ page }) => {
  await openWorkspaceWithSentOffer(page);
  await page.getByRole("button", { name: /new draft/i }).click();
  await expect(page.getByText(/previous offers/i)).toBeVisible();
  await expect(page.getByText(/sent/i).last()).toBeVisible();
  await expect(page.getByLabel(/customer note/i)).toHaveValue("");
});
```

- [ ] **Step 2: Run the two tests and verify RED**

```powershell
npx playwright test e2e/17-admin-workspace.spec.ts --grep "draft|compact history"
```

Expected: missing delete endpoint/client/control and latest-offer selection loads closed offers incorrectly.

- [ ] **Step 3: Extract the editing model and offer stage**

Move the existing editable-option helpers unchanged into `leadWorkspaceModels.ts`, changing `matchToEditable` to `candidateToEditable(candidate)` so it carries `candidate.locationId` into `supplierLocationId`.

Use this component contract:

```typescript
interface LeadOfferStageProps {
  lead: AdminLead;
  offers: AdminOffer[];
  outreachRows: ProviderOutreachRow[];
  candidateToAdd: ProviderCandidate | null;
  onCandidateConsumed(): void;
  onOffersChanged(): void;
}
```

Choose the active draft with `offers.find(o => o.status === "draft")`, not the newest offer. Sort all non-drafts descending into compact history. Create reuses the backend draft. Delete uses `adminOfferService.remove(id)`, an `AlertDialog`, and invalidates offers. Preserve current replace-set saving, empty-string note clearing, ordering, and conflict refresh behavior. Never render a public draft URL.

Render outreach history above the editor with status and notes controls. A `Replied` row gets a prominent `Add to offer` action that creates an editable option using its `supplierId` and `supplierName`; price, unit, location, and notes remain editable. Keep the existing free-form `Add option` action for telephone quotes and providers outside the directory.

On delete 404, close the editor, clear the local draft buffer, and refetch offers. On delete or save 409, keep the user's unsaved buffer visible, refetch offer history, and show the server message. No error path may silently create a replacement draft.

- [ ] **Step 4: Extend service and fixture behavior**

```typescript
async remove(id: string): Promise<void> {
  await apiClient.delete(`/admin/offers/${id}`);
}
```

The stateful fixture must reuse an existing Draft on POST, remove only Draft on DELETE, return 409 for other statuses, and keep closed offers in its array.

- [ ] **Step 5: Add five-language draft copy**

Add the offer-stage keys with the exact five-language values in Appendix A.

- [ ] **Step 6: Run targeted tests and static checks**

```powershell
npx playwright test e2e/17-admin-workspace.spec.ts --grep "draft|offer builder|customer note"
npm test -- src/test/translations.test.ts
npx tsc --noEmit
```

Expected: all commands pass.

- [ ] **Step 7: Commit in the frontend repository**

```powershell
git add src/components/admin/leads/leadWorkspaceModels.ts src/components/admin/leads/LeadOfferStage.tsx src/components/admin/AdminLeads.tsx src/services/index.ts src/i18n/translations.ts e2e/fixtures.ts e2e/17-admin-workspace.spec.ts
git commit -m "feat: manage one deletable lead offer draft"
```

### Task 8: Exact delivery review, extracted workspace, and booking confirmation

**Files:**
- Create: `estonia-space-hub/src/components/admin/leads/LeadDeliveryReview.tsx`
- Create: `estonia-space-hub/src/components/admin/leads/LeadActivityTimeline.tsx`
- Create: `estonia-space-hub/src/components/admin/leads/LeadWorkspace.tsx`
- Modify: `estonia-space-hub/src/components/admin/AdminLeads.tsx`
- Modify: `estonia-space-hub/src/services/index.ts`
- Modify: `estonia-space-hub/src/services/queryKeys.ts`
- Modify: `estonia-space-hub/src/i18n/translations.ts`
- Modify: `estonia-space-hub/e2e/fixtures.ts`
- Modify: `estonia-space-hub/e2e/17-admin-workspace.spec.ts`

**Interfaces:**
- Consumes Task 3 delivery preview, Task 4 confirmation, and Task 5 `OfferPresentation`.
- Produces: the final three-stage `LeadWorkspace` rendered by `AdminLeads`.

- [ ] **Step 1: Write failing exact-preview and confirmation E2E tests**

```typescript
test("review delivery shows exact email and page before sending", async ({ page }) => {
  await openWorkspaceWithDraft(page);
  await page.getByRole("button", { name: /review delivery/i }).click();
  await expect(page.getByRole("dialog")).toContainText("mari@example.com");
  await expect(page.getByRole("dialog")).toContainText("Your Ruumly options");
  await expect(page.getByRole("dialog").getByText("Miniladu 10 m² kesklinnas")).toBeVisible();
  await expect(page.getByRole("dialog")).toContainText(/lead moves to quoted/i);
  await page.getByRole("dialog").getByRole("button", { name: /send to customer/i }).click();
  await expect(page.getByText(/offer sent/i)).toBeVisible();
});

test("chosen preference needs explicit provider confirmation", async ({ page }) => {
  await openWorkspaceWithChosenOffer(page);
  await expect(page.getByText(/customer requested/i)).toBeVisible();
  await page.getByRole("button", { name: /confirm with provider and mark booked/i }).click();
  await page.getByRole("alertdialog").getByRole("button", { name: /mark booked/i }).click();
  await expect(page.getByText(/booking outcome confirmed/i)).toBeVisible();
});

for (const viewport of [{ width: 375, height: 812 }, { width: 1440, height: 900 }]) {
  test(`guided workspace fits ${viewport.width}px without clipped actions`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await openWorkspace(page);
    await expect(page.getByText(/find and contact providers/i)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({
      path: `test-results/lead-workspace-${viewport.width}.png`,
      fullPage: true,
    });
  });
}
```

- [ ] **Step 2: Run and verify RED**

```powershell
npx playwright test e2e/17-admin-workspace.spec.ts --grep "review delivery|provider confirmation"
```

Expected: no delivery-preview client/UI and no confirmation control.

- [ ] **Step 3: Add typed service contracts**

```typescript
export interface OfferDeliveryPreview {
  recipient: { name: string | null; email: string };
  email: { subject: string; textBody: string };
  page: PublicOffer;
}

async deliveryPreview(id: string): Promise<OfferDeliveryPreview> {
  return apiClient.get(`/admin/offers/${id}/delivery-preview`);
},
async confirmBooking(id: string): Promise<AdminOffer> {
  return apiClient.post(`/admin/offers/${id}/confirm-booking`, {});
}
```

Add `queryKeys.adminLeads.deliveryPreview(id)`.

- [ ] **Step 4: Build delivery review and confirmation UI**

`LeadDeliveryReview` receives the active Draft or Chosen offer, fetches preview only when opened, and renders tabs `Email` and `Customer page`. The email tab shows recipient, subject, and a whitespace-preserving body. The page tab renders `<OfferPresentation offer={preview.page} preview />` without an action. Preview never navigates to the token URL.

Preview failure leaves the Draft untouched and renders a retry action inside the dialog. HTTP 400 send validation keeps the dialog open and shows the backend requirement beside the final action. HTTP 409 invalidates offers and reloads the chosen/closed state. Confirmation 409 keeps the lead Quoted and refreshes the chosen option instead of displaying a success toast.

The send confirmation lists these exact effects as visible bullets: email is sent to the displayed address; customer link becomes live; lead moves to Quoted; opening records Viewed; requesting an option alerts Ruumly; no payment or confirmed booking occurs. Only its final action persists the current draft and calls send.

When `offer.status === "chosen"`, show the chosen option and `Confirm with provider and mark booked`. Confirm through an `AlertDialog`, call `confirmBooking`, and invalidate lead root, offers, metrics, and timeline data.

- [ ] **Step 5: Extract workspace and timeline**

Move orchestration, lead status/edit controls, note mutations, candidate handoff, and shared query invalidation into `LeadWorkspace.tsx`. `AdminLeads.tsx` retains metrics, filters, pagination, and expansion. At `md` and above, keep the existing table; below `md`, render each lead as a compact full-width card with the same expand action so the workspace is not constrained by an eight-column table. Move the current derived timeline into `LeadActivityTimeline.tsx`. Add `Customer requested` at `offer.chosenAt`; show Converted as the current read-only state badge, not as a fabricated timeline event, because the existing model has no confirmation timestamp.

The workspace header shows lead age, category, city/route, requested date, and response SLA before the three numbered stages. Long email addresses, subjects, routes, and provider names use wrapping rather than horizontal scrolling.

Remove `converted` from the clickable pipeline and row status dropdown. Keep it as a filter and read-only badge. Only `confirmBooking` may move a lead to Converted.

- [ ] **Step 6: Add exact five-language delivery copy**

Add the delivery-stage keys with the exact five-language values in Appendix A.

- [ ] **Step 7: Extend stateful E2E fixture**

Return exact delivery preview data without mutating offer status; send updates Draft to Sent; confirm-booking requires Chosen and changes the mocked lead to Converted. Record request bodies so tests can assert no send occurs before the confirmation action.

- [ ] **Step 8: Run admin E2E, translation, TypeScript, and build**

```powershell
npx playwright test e2e/17-admin-workspace.spec.ts
npm test -- src/test/translations.test.ts
npx tsc --noEmit
npm run build
```

Expected: all commands pass.

- [ ] **Step 9: Commit in the frontend repository**

```powershell
git add src/components/admin/leads/LeadDeliveryReview.tsx src/components/admin/leads/LeadActivityTimeline.tsx src/components/admin/leads/LeadWorkspace.tsx src/components/admin/AdminLeads.tsx src/services/index.ts src/services/queryKeys.ts src/i18n/translations.ts e2e/fixtures.ts e2e/17-admin-workspace.spec.ts
git commit -m "feat: review lead delivery and confirm booking outcomes"
```

### Task 9: Documentation, complete verification, and release evidence

**Files:**
- Modify: `docs/CONCIERGE-OPS.md`
- Modify: `docs/ROADMAP.md`
- Verify: all backend and frontend files changed by Tasks 1-8

**Interfaces:**
- Documents the shipped state flow and operator runbook.
- Produces release evidence for backend-first deployment and production canary.

- [ ] **Step 1: Update operational documentation**

Document this exact state flow in `CONCIERGE-OPS.md`:

```text
New -> outreach sent -> Contacted
Contacted -> offer sent -> Quoted / Offer Sent
Quoted -> customer opens -> Quoted / Offer Viewed
Quoted -> customer requests option -> Quoted / Offer Chosen
Quoted -> admin confirms provider -> Converted
```

Add operator steps for 25 km nearby discovery, All Estonia search, outreach review, explicit resend, one active draft, draft deletion, exact delivery preview, pending customer preference, and provider confirmation. In `ROADMAP.md`, mark the lead-operations polish item complete without changing the demand-first strategy or adding automatic payments/contracts.

- [ ] **Step 2: Run the complete backend verification**

From `Ruumly.Backend/`:

```powershell
dotnet build
dotnet test ..\Ruumly.Backend.Tests\Ruumly.Backend.Tests.csproj
```

Expected: build succeeds with 0 errors; all tests pass.

- [ ] **Step 3: Run the complete frontend verification**

From `estonia-space-hub/`:

```powershell
npx tsc --noEmit -p tsconfig.app.json
npx tsc --noEmit -p e2e/tsconfig.json
npm run lint
npm test
npm run build
npm run test:e2e
```

Expected: every command exits 0. Any pre-existing warning must be recorded; errors are not acceptable.

- [ ] **Step 4: Verify responsive layout with Playwright screenshots**

Run the authenticated, fully mocked admin workspace screenshot cases at both viewports, then the existing global mobile-overflow suite:

```powershell
npx playwright test e2e/17-admin-workspace.spec.ts --grep "without clipped actions" --project=chromium
npx playwright test e2e/14-mobile-overflow.spec.ts --project=chromium
```

Inspect `test-results/lead-workspace-375.png` and `test-results/lead-workspace-1440.png` for clipped dialog actions, horizontal overflow, nested-card clutter, and touch targets below 44 px.

- [ ] **Step 5: Commit documentation**

```powershell
git add docs/CONCIERGE-OPS.md docs/ROADMAP.md
git commit -m "docs: update concierge lead operations runbook"
```

- [ ] **Step 6: Deploy backend first and run a safe canary**

After branch review and merge, wait for Railway health to pass. Create a dedicated test lead, then verify candidate count, outreach preview side-effect freedom, draft reuse/delete, delivery preview, pending choice, and admin confirmation. Do not send provider/customer email or delete production data without action-time confirmation.

- [ ] **Step 7: Deploy frontend and run live mobile E2E**

After Vercel deploy is healthy, verify the admin lead workspace at 375 px and desktop, then complete the dedicated test lead workflow. Confirm Sentry, Railway logs, and browser console contain no new errors. Only after the canary passes should the real lead be opened in the new workspace.

## Appendix A: Exact Operator Translation Values

Add every row to all five flat language dictionaries in `src/i18n/translations.ts`. Preserve `{count}` and `{email}` exactly.

### Provider stage

| Key | et | en | ru | lv | lt |
|---|---|---|---|---|---|
| `admin.leads.stageProviders` | Leia ja võta partneritega ühendust | Find and contact providers | Найдите поставщиков и свяжитесь с ними | Atrodi pakalpojumu sniedzējus un sazinies | Raskite paslaugų teikėjus ir susisiekite |
| `admin.leads.searchProviders` | Otsi partnereid, linnu, aadresse või kontakte | Search providers, cities, addresses or contacts | Поиск по поставщикам, городам, адресам или контактам | Meklē pakalpojumu sniedzējus, pilsētas, adreses vai kontaktus | Ieškokite paslaugų teikėjų, miestų, adresų ar kontaktų |
| `admin.leads.scopeNearby` | Läheduses | Nearby | Рядом | Tuvumā | Netoliese |
| `admin.leads.scopeAll` | Kogu Eesti | All Estonia | Вся Эстония | Visa Igaunija | Visa Estija |
| `admin.leads.allServices` | Kõik teenused | All services | Все услуги | Visi pakalpojumi | Visos paslaugos |
| `admin.leads.radiusKm` | {count} km | {count} km | {count} км | {count} km | {count} km |
| `admin.leads.distanceUnavailable` | Vahemaa pole saadaval | Distance unavailable | Расстояние недоступно | Attālums nav pieejams | Atstumas nepasiekiamas |
| `admin.leads.otherLocations` | Muud asukohad | Other locations | Другие адреса | Citas atrašanās vietas | Kitos vietos |
| `admin.leads.noEmail` | E-posti aadress puudub | No email address | Нет адреса электронной почты | Nav e-pasta adreses | Nėra el. pašto adreso |
| `admin.leads.alreadyContacted` | Juba ühendust võetud | Already contacted | Уже связались | Jau sazinājāmies | Jau susisiekta |
| `admin.leads.reviewProviders` | Vaata üle sõnum {count} partnerile | Review message to {count} providers | Проверить сообщение для {count} поставщиков | Pārskatīt ziņojumu {count} pakalpojumu sniedzējiem | Peržiūrėti žinutę {count} paslaugų teikėjams |
| `admin.leads.reviewMessageTitle` | Vaata partneripäring üle | Review provider outreach | Проверить обращение к поставщикам | Pārskatīt ziņojumu pakalpojumu sniedzējiem | Peržiūrėti užklausą paslaugų teikėjams |
| `admin.leads.recipient` | Saaja | Recipient | Получатель | Saņēmējs | Gavėjas |
| `admin.leads.subject` | Teema | Subject | Тема | Temats | Tema |
| `admin.leads.message` | Sõnum | Message | Сообщение | Ziņojums | Žinutė |
| `admin.leads.sendOutreach` | Saada saadavuspäring | Send availability request | Отправить запрос о доступности | Nosūtīt pieejamības pieprasījumu | Siųsti prieinamumo užklausą |
| `admin.leads.resendOutreach` | Saada saadavuspäring uuesti | Resend availability request | Повторно отправить запрос о доступности | Atkārtoti nosūtīt pieejamības pieprasījumu | Pakartotinai siųsti prieinamumo užklausą |
| `admin.leads.skipAlreadyContacted` | Varem kontaktitud partnerid jäetakse vahele | Previously contacted providers will be skipped | Ранее опрошенные поставщики будут пропущены | Iepriekš uzrunātie pakalpojumu sniedzēji tiks izlaisti | Anksčiau kontaktuoti paslaugų teikėjai bus praleisti |

### Offer stage

| Key | et | en | ru | lv | lt |
|---|---|---|---|---|---|
| `admin.leads.stageOffer` | Koosta kliendi valikud | Build customer options | Создать варианты для клиента | Izveidot klienta variantus | Kurti variantus klientui |
| `admin.leads.activeDraft` | Aktiivne mustand | Active draft | Активный черновик | Aktīvais melnraksts | Aktyvus juodraštis |
| `admin.leads.oneActiveDraft` | Üks aktiivne mustand | One active draft | Один активный черновик | Viens aktīvs melnraksts | Vienas aktyvus juodraštis |
| `admin.leads.previousOffers` | Varasemad pakkumised | Previous offers | Предыдущие предложения | Iepriekšējie piedāvājumi | Ankstesni pasiūlymai |
| `admin.leads.newDraft` | Uus mustand | New draft | Новый черновик | Jauns melnraksts | Naujas juodraštis |
| `admin.leads.deleteDraft` | Kustuta mustand | Delete draft | Удалить черновик | Dzēst melnrakstu | Ištrinti juodraštį |
| `admin.leads.deleteDraftTitle` | Kas kustutada see mustand? | Delete this draft? | Удалить этот черновик? | Dzēst šo melnrakstu? | Ištrinti šį juodraštį? |
| `admin.leads.deleteDraftBody` | Mustand ja selle valikud kustutatakse jäädavalt. | The draft and its options will be permanently removed. | Черновик и его варианты будут удалены безвозвратно. | Melnraksts un tā varianti tiks neatgriezeniski dzēsti. | Juodraštis ir jo variantai bus negrįžtamai ištrinti. |
| `admin.leads.draftDeleted` | Mustand kustutatud | Draft deleted | Черновик удалён | Melnraksts dzēsts | Juodraštis ištrintas |
| `admin.leads.addToOffer` | Lisa pakkumisse | Add to offer | Добавить в предложение | Pievienot piedāvājumam | Pridėti prie pasiūlymo |

### Delivery stage

| Key | et | en | ru | lv | lt |
|---|---|---|---|---|---|
| `admin.leads.stageDelivery` | Vaata üle ja saada | Review and send | Проверить и отправить | Pārskatīt un nosūtīt | Peržiūrėti ir siųsti |
| `admin.leads.reviewDelivery` | Vaata saatmine üle | Review delivery | Проверить отправку | Pārskatīt nosūtīšanu | Peržiūrėti siuntimą |
| `admin.leads.emailPreview` | E-kiri | Email | Электронное письмо | E-pasts | El. laiškas |
| `admin.leads.pagePreview` | Kliendi leht | Customer page | Страница клиента | Klienta lapa | Kliento puslapis |
| `admin.leads.sendEffectsTitle` | Saatmisel toimub: | Sending will: | При отправке произойдёт следующее: | Nosūtot notiks: | Išsiuntus bus: |
| `admin.leads.effectEmail` | Saada e-kiri aadressile {email} | Send email to {email} | Отправить письмо на {email} | Nosūtīt e-pastu uz {email} | Siųsti el. laišką adresu {email} |
| `admin.leads.effectLive` | Muuda kliendi link aktiivseks | Make the customer link live | Активировать ссылку клиента | Aktivizēt klienta saiti | Aktyvinti kliento nuorodą |
| `admin.leads.effectQuoted` | Muuda päringu staatuseks Hinnastatud | Move the lead to Quoted | Перевести запрос в статус «Предложение отправлено» | Mainīt pieprasījuma statusu uz Piedāvāts | Pakeisti užklausos būseną į Pasiūlyta |
| `admin.leads.effectViewed` | Märgi avatuks, kui klient lingi avab | Record Viewed when the customer opens the link | Отметить просмотр, когда клиент откроет ссылку | Atzīmēt kā skatītu, kad klients atver saiti | Pažymėti kaip peržiūrėtą, kai klientas atidaro nuorodą |
| `admin.leads.effectRequested` | Salvesta ootel soov ja teavita Ruumlyt | Record a pending preference and alert Ruumly | Сохранить ожидающий выбор и уведомить Ruumly | Saglabāt gaidošu izvēli un paziņot Ruumly | Išsaugoti laukiamą pasirinkimą ir pranešti Ruumly |
| `admin.leads.effectNoBooking` | Makset ei võeta ega kinnitatud broneeringut ei looda | Not take payment or create a confirmed booking | Не списывать оплату и не создавать подтверждённое бронирование | Neiekasēt maksājumu un neveidot apstiprinātu rezervāciju | Nenuskaityti mokėjimo ir nekurti patvirtinto užsakymo |
| `admin.leads.customerRequested` | Klient soovis seda valikut | Customer requested this option | Клиент запросил этот вариант | Klients pieprasīja šo variantu | Klientas užklausė šio varianto |
| `admin.leads.confirmBooking` | Kinnita partneriga ja märgi broneerituks | Confirm with provider and mark booked | Подтвердить у поставщика и отметить забронированным | Apstiprināt ar pakalpojumu sniedzēju un atzīmēt kā rezervētu | Patvirtinti su paslaugų teikėju ir pažymėti užsakytu |
| `admin.leads.confirmBookingTitle` | Kas kinnitada broneeringu tulemus? | Confirm this booking outcome? | Подтвердить результат бронирования? | Apstiprināt rezervācijas rezultātu? | Patvirtinti užsakymo rezultatą? |
| `admin.leads.confirmBookingBody` | Kasuta seda ainult pärast seda, kui partner on saadavuse kinnitanud. | Use this only after the provider confirms availability. | Используйте это только после подтверждения доступности поставщиком. | Izmanto tikai pēc tam, kad pakalpojumu sniedzējs ir apstiprinājis pieejamību. | Naudokite tik paslaugų teikėjui patvirtinus prieinamumą. |
| `admin.leads.markBooked` | Märgi broneerituks | Mark booked | Отметить забронированным | Atzīmēt kā rezervētu | Pažymėti užsakytu |
| `admin.leads.bookingConfirmed` | Broneeringu tulemus kinnitatud | Booking outcome confirmed | Результат бронирования подтверждён | Rezervācijas rezultāts apstiprināts | Užsakymo rezultatas patvirtintas |
