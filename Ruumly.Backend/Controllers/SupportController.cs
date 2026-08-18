using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api")]
public class SupportController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    INotificationService notificationService,
    IConfiguration config,
    IConciergeOutreachService outreachService,
    ILogger<SupportController> logger) : ControllerBase
{
    /// <summary>
    /// A need date further out than this is a typo, not a plan (usually a
    /// mistyped year). Rejected rather than clamped so the visitor sees it.
    /// </summary>
    private const int MaxNeedDateYearsAhead = 2;

    /// <summary>
    /// How long an identical concierge request is treated as a repeat of the
    /// first one rather than a new request. See <see cref="RequestConcierge"/>.
    /// </summary>
    private static readonly TimeSpan DuplicateRequestWindow = TimeSpan.FromMinutes(10);

    // ─── Automation signals ──────────────────────────────────────────────────
    //
    // WHAT IS ACTUALLY AT RISK. Since auto fan-out shipped, one POST to this
    // anonymous endpoint sends up to six emails to real third-party businesses.
    // That turns ordinary form spam into outbound-email amplification aimed at
    // the supply base — the thing a 754-address campaign was spent building — and
    // at Ruumly's own sending reputation. Rate limiting alone (5 per 10 min per
    // IP) does not bound that.
    //
    // WHAT THIS DOES, AND WHAT IT DELIBERATELY DOES NOT. A suspected bot's lead
    // is still SAVED, in full, with status New. Only the automatic fan-out is
    // withheld, and the ops alert says exactly why, so an operator reviews it and
    // contacts providers by hand if it is real. Silently dropping a submission
    // would mean a real customer gets a success screen and Ruumly gets nothing —
    // strictly worse than the spam we are guarding against.
    //
    // No CAPTCHA: it taxes every real customer on a funnel whose entire value
    // proposition is that it is short. These signals cost a human nothing.

    /// <summary>Minimum plausible time to complete the three-step funnel.</summary>
    private const int MinFormSeconds = 4;

    /// <summary>
    /// How many concierge requests one email address may auto-fan-out in a day.
    /// The signal that needs no client cooperation, and therefore the only one a
    /// determined attacker cannot simply omit from a hand-rolled POST.
    /// </summary>
    private const int MaxAutoFanOutPerEmailPerDay = 5;

    /// <summary>
    /// Public contact form. Emails the team the visitor's message.
    /// Delivery is queued so transient provider failures are retried without
    /// delaying or failing the visitor's request.
    ///
    /// When the message was written on a partner's public page it also carries
    /// that partner's slug, and then it is DEMAND rather than correspondence —
    /// see <see cref="CapturePartnerMessageAsync"/>, which turns it into a routed
    /// <see cref="DemandLead"/> and delivers it to the partner when we are
    /// allowed to write to them.
    /// </summary>
    [HttpPost("contact")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Contact([FromBody] ContactRequest req)
    {
        // Validated for the same reason the lead intakes validate it: the address
        // is printed into the ops mail as the ONLY way back to this person. A
        // typo'd one turns a real question into an unanswerable note.
        if (!EmailValidation.IsValid(req.Email))
            return BadRequest(new { error = "Invalid email." });

        // Resolve the team inbox from PlatformSettings; fall back to the
        // public contact address used elsewhere in the app.
        var teamEmail = await db.PlatformSettings
            .Where(s => s.Key == "siteEmail")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(teamEmail))
            teamEmail = "info@ruumly.eu";

        var lang = string.IsNullOrWhiteSpace(req.Language) ? "et" : req.Language;

        // Null for the ordinary /contact page, which sends no slug — the ops mail
        // below is then byte-for-byte what it always was.
        var partnerReport = await CapturePartnerMessageAsync(req, lang);

        emailQueue.EnqueueEmail(
            to:       teamEmail,
            subject:  $"[Ruumly contact] {req.Subject}",
            textBody: $"From: {req.Name} <{req.Email}>\nLang: {lang}\n{partnerReport}\n{req.Message}\n\n— Reply directly to {req.Email}");

        return Ok(new { success = true });
    }

    /// <summary>
    /// Captures a message written on <c>/{lang}/partner/{slug}</c> as a routed
    /// <see cref="DemandLead"/> (<c>Source = "partner-page"</c>) and delivers it
    /// to that partner when — and only when — they are someone we may write to.
    ///
    /// WHY THIS EXISTS. The dialog on a partner page tells the sender, in all five
    /// languages, that the partner will get back to them. It posted here, the team
    /// inbox got one untracked email, and the partner was never told: both people
    /// who used it (Peetri Miniladu 2026-08-16, GREENAS UAB 2026-08-17) are still
    /// waiting. And the message is not a support question — someone asking a
    /// self-storage company in Peetri about space is a customer request inside the
    /// Tallinn concierge catchment, which is the only kind of event this business
    /// is currently trying to produce.
    ///
    /// Returns the block the ops alert must carry, so a human can see at a glance
    /// whether the partner already has the message or ops has to relay it by hand;
    /// null when the request did not come from a partner page. NEVER throws and
    /// never fails the visitor: everything here is strictly better than the old
    /// behaviour, and none of it is worth a 500 on a message we could still email.
    /// </summary>
    private async Task<string?> CapturePartnerMessageAsync(ContactRequest req, string lang)
    {
        if (string.IsNullOrWhiteSpace(req.PartnerSlug)) return null;

        var slug = req.PartnerSlug.Trim();
        try
        {
            // Same shape guard the public profile endpoint applies before it
            // touches the database — the slug reaches us from the same URL, so a
            // caller who invents one gets the same flat refusal there and here.
            if (slug.Length is < 2 or > 80 || !Regex.IsMatch(slug, "^[a-z0-9-]+$"))
                return Unresolved(slug);

            // Same visibility rule the public profile endpoint enforces
        // (SupplierProfileService.GetBySlugAsync): active AND published. Matching
        // on IsActive alone would let a row that has no reachable page capture a
        // lead, which can only happen through a hand-rolled POST — a divergence
        // with no legitimate caller behind it.
        var supplier = await db.Suppliers.FirstOrDefaultAsync(
            s => s.Slug == slug && s.IsActive && s.IsPartnerPagePublished);
            if (supplier is null)
                return Unresolved(slug);

            // What the message is ABOUT. The visitor chose a company, not a
            // service, so the partner's own catalogue is the only evidence there
            // is — and it is evidence only when it says one thing. A partner that
            // sells three services tells us nothing about this message, and Any is
            // the honest answer: an admin re-categorises it in one click, whereas a
            // confidently wrong category quietly routes the follow-up to the wrong
            // providers.
            var services = ServiceCategories.ParseServiceTypes(supplier.ServiceTypesJson)
                .Select(s => s?.Trim().ToLowerInvariant() ?? "")
                .Where(ServiceCategories.IsConsumerSlug)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // The visitor never typed a city, and City drives every geographic
            // surface we have (admin list, provider match radius). The partner's
            // own primary site is the only honest stand-in — same rule the claim
            // flow uses for the same reason.
            var city = await db.SupplierLocations
                .Where(l => l.SupplierId == supplier.Id && l.IsActive && !l.IsSynthetic)
                .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
                .Select(l => l.City)
                .FirstOrDefaultAsync() ?? "";

            var message = req.Message?.Trim() ?? "";

            var lead = new DemandLead
            {
                Id         = Guid.NewGuid(),
                Email      = req.Email.Trim(),
                Name       = Trimmed(req.Name, 120),
                City       = Trimmed(city, 100) ?? "",
                Category   = services.Count == 1
                    ? ServiceCategories.BySlug[services[0]]
                    : DemandLeadCategory.Any,
                SupplierId = supplier.Id,
                // Query is the admin list view's one-liner and stops at 500 chars,
                // but the form accepts 5000 — so the customer's own words are also
                // kept whole in Details, which is where a later hand-run outreach
                // would read them from.
                Query      = Trimmed(message, 500),
                Details    = Trimmed(message, 2000),
                Language   = lang,
                Status     = DemandLeadStatus.New,
                // Its own tag: addressed to ONE named company from that company's
                // page, so it is neither the concierge funnel (whose north-star
                // metrics filter on Source=="concierge" and must not absorb this)
                // nor a listing-routed quote.
                Source     = "partner-page",
                CreatedAt  = DateTime.UtcNow,
            };

            db.DemandLeads.Add(lead);
            await db.SaveChangesAsync();

            // ── May we write to this partner at all? ──────────────────────────
            // Nearly all of the ~1187 supplier rows are SCRAPED directory imports:
            // a public page exists for them, but nobody behind it ever asked to
            // hear from us. Forwarding a visitor's message to one of those is a
            // cold email we send on a stranger's behalf to a business whose only
            // relationship with Ruumly is that we listed it.
            //
            // So delivery needs a CLAIMED partner — someone who proved control of
            // ContactEmail through /{lang}/claim/{slug}, which is exactly what
            // mints the Provider user this looks for. The two flags below outrank
            // even that: an opt-out is a promise we made in writing, and a bounced
            // address cannot be reached and costs sending reputation to retry.
            //
            // Note what is deliberately ABSENT: IConciergeOutreachService. A
            // partner-page message names one company; fanning it out would
            // cold-email that company's competitors off the back of a note a
            // customer wrote to it.
            var providerUserIds = await db.Users
                .Where(u => u.SupplierId == supplier.Id && u.Role == UserRole.Provider)
                .Select(u => u.Id)
                .ToListAsync();

            var withheld =
                providerUserIds.Count == 0                       ? "unclaimed directory profile" :
                supplier.MarketingOptOutAt is not null           ? "partner opted out of contact" :
                supplier.ContactEmailUnusable                    ? "partner's email address has bounced" :
                string.IsNullOrWhiteSpace(supplier.ContactEmail) ? "no contact address on file" :
                null;

            if (withheld is null)
            {
                foreach (var uid in providerUserIds)
                    await notificationService.CreateAsync(
                        uid, NotificationType.Order,
                        title:      "New message from your Ruumly page",
                        desc:       $"{lead.Name ?? lead.Email} — {req.Subject}",
                        actionUrl:  "/provider/leads",
                        entityId:   lead.Id.ToString(),
                        entityType: "lead");

                emailQueue.EnqueueEmail(
                    to:       supplier.ContactEmail,
                    subject:  $"New message from your Ruumly page — {req.Subject}",
                    textBody: $"Name: {lead.Name}\nEmail: {lead.Email}\n\n{message}\n\n" +
                              $"Reply to this mail to answer {lead.Email} directly, " +
                              "or respond from your Ruumly dashboard → Leads.",
                    htmlBody: null,
                    // Reply-To the CUSTOMER, not ops: the partner page promised
                    // this person the partner would get back to them, and hitting
                    // reply is the shortest path from that promise to a kept one.
                    // (Provider cold outreach points Reply-To at ops instead —
                    // there the reply is an answer to us, not to a stranger.)
                    replyTo:  lead.Email);
            }
            else
            {
                logger.LogInformation(
                    "Partner-page lead {LeadId} for supplier {SupplierId} not delivered: {Reason}.",
                    lead.Id, supplier.Id, withheld);
            }

            var appUrl    = string.IsNullOrWhiteSpace(config["AppUrl"]) ? "https://ruumly.eu" : config["AppUrl"];
            var adminLink = FrontendUrl.Localized(appUrl, "et", $"admin?tab=leads&lead={lead.Id}");

            return $"Partner page: {supplier.Name} ({slug})\n" +
                   (withheld is null
                       ? "Partner notified: yes — email + provider dashboard\n"
                       : $"Partner notified: NO ({withheld}) — RELAY THIS BY HAND\n") +
                   $"Lead: {ServiceCategories.SlugFor(lead.Category)}" +
                   (lead.City.Length > 0 ? $" in {lead.City}" : "") + ", Source=partner-page\n" +
                   $"Open the workspace: {adminLink}\n";
        }
        catch (Exception ex)
        {
            // The visitor's message still reaches ops through the mail this
            // returns into, which is everything the old code ever did.
            logger.LogError(ex,
                "Partner-page message capture failed for slug {Slug} — falling back to an ops email only.",
                slug);
            return $"Partner page: {slug} — CAPTURE FAILED (see logs). " +
                   "Check the CRM for a lead, then relay this by hand.\n";
        }

        static string Unresolved(string slug) =>
            $"Partner page: \"{slug}\" matched no active partner — no lead created, ops email only.\n";
    }

    /// <summary>Trimmed, clamped to <paramref name="max"/> chars; null when empty.</summary>
    private static string? Trimmed(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>
    /// Public "request a quote" from a specific listing (moving/trailer are
    /// quote-first, one-time services). Unlike the generic contact form, this
    /// captures a routed <see cref="DemandLead"/> tied to the listing's partner
    /// and notifies that partner directly — so the request reaches the person
    /// who can price it instead of vanishing into a shared inbox. Anonymous by
    /// design: there is no account or payment at this stage.
    /// </summary>
    [HttpPost("leads/quote")]
    [AllowAnonymous]
    [EnableRateLimiting("public-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestQuote([FromBody] QuoteLeadRequest req)
    {
        if (!EmailValidation.IsValid(req.Email))
            return BadRequest(new { error = "Invalid email." });

        var listing = await db.Listings
            .Include(l => l.Supplier)
            .Include(l => l.Location)
            .FirstOrDefaultAsync(l => l.Id == req.ListingId && l.IsActive);
        if (listing is null)
            return NotFound(new { error = "Listing not found." });

        var lang = string.IsNullOrWhiteSpace(req.Language) ? "et" : req.Language!;
        var city = (req.City ?? listing.Location?.City ?? listing.City ?? "").Trim();

        var lead = new DemandLead
        {
            Id         = Guid.NewGuid(),
            Email      = req.Email.Trim(),
            Name       = req.Name?.Trim(),
            Phone      = req.Phone?.Trim(),
            City       = city.Length > 100 ? city[..100] : city,
            Category   = listing.Type switch
            {
                ListingType.Moving    => DemandLeadCategory.Moving,
                ListingType.Trailer   => DemandLeadCategory.Trailer,
                ListingType.Warehouse => DemandLeadCategory.Warehouse,
                _                     => DemandLeadCategory.Any,
            },
            SupplierId = listing.SupplierId,
            ListingId  = listing.Id,
            Query      = req.Message is { Length: > 500 } longMsg ? longMsg[..500] : req.Message,
            Language   = lang,
            Status     = DemandLeadStatus.New,
            // Routed to a specific partner from a listing — NOT the concierge
            // demand funnel. Tagged so the north-star metrics (Source=="concierge")
            // isolate cleanly and never count partner-direct quote requests.
            Source     = "routed",
            CreatedAt  = DateTime.UtcNow,
        };

        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        // Notify the partner in-app (each of their provider logins) so the lead
        // surfaces in the dashboard's Leads view, and by email as a fallback.
        var providerUserIds = await db.Users
            .Where(u => u.SupplierId == listing.SupplierId && u.Role == UserRole.Provider)
            .Select(u => u.Id)
            .ToListAsync();
        foreach (var uid in providerUserIds)
            await notificationService.CreateAsync(
                uid, NotificationType.Order,
                title:      "New quote request",
                desc:       $"{lead.Name ?? lead.Email} — {listing.Title}",
                actionUrl:  "/provider/leads",
                entityId:   lead.Id.ToString(),
                entityType: "lead");

        if (!string.IsNullOrWhiteSpace(listing.Supplier?.ContactEmail))
            emailQueue.EnqueueEmail(
                to:       listing.Supplier.ContactEmail,
                subject:  $"New quote request — {listing.Title}",
                textBody: $"Name: {lead.Name}\nEmail: {lead.Email}\nPhone: {lead.Phone}\nCity: {city}\n\n{req.Message}\n\nRespond from your Ruumly dashboard → Leads.");

        emailQueue.EnqueueEmail(
            to:       await OpsInbox.ResolveAsync(db),
            subject:  $"New routed quote lead — {listing.Title}",
            textBody: $"Supplier: {listing.Supplier?.Name}\nFrom: {lead.Name} <{lead.Email}> {lead.Phone}\nCity: {city}\nCategory: {lead.Category}\n\n{req.Message}");

        return Ok(new { success = true });
    }

    /// <summary>
    /// Public concierge intake — the demand-first pivot's front door. The visitor
    /// describes what they need (categories, from/to city, date, free-text details)
    /// without picking a listing; we store it as a <see cref="DemandLead"/> with
    /// <c>Source = "concierge"</c> and the admin team finds/contacts a partner.
    /// Anonymous by design: no account, no listing, no payment at this stage.
    /// </summary>
    [HttpPost("leads/request")]
    [AllowAnonymous]
    [EnableRateLimiting("public-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestConcierge([FromBody] ConciergeRequest req)
    {
        if (!EmailValidation.IsValid(req.Email))
            return BadRequest(new { error = "Invalid email." });
        if (string.IsNullOrWhiteSpace(req.City))
            return BadRequest(new { error = "City is required." });

        // A date in the past is never a real request, and it is not harmless:
        // ProviderOutreachComposer flags anything within three days (past
        // included) as URGENT, in the subject line and in a coloured banner. So a
        // visitor who mistypes the year sends a red-flagged cold email to real
        // businesses about a job that already happened — spending the one cold
        // contact we get with each of them on a request nobody can take. The far
        // bound catches the other direction of the same typo.
        if (req.NeedDate is { } requested)
        {
            var day = requested.Date;
            if (day < DateTime.UtcNow.Date)
                return BadRequest(new { error = "Need date cannot be in the past." });
            if (day > DateTime.UtcNow.Date.AddYears(MaxNeedDateYearsAhead))
                return BadRequest(new { error = "Need date is too far in the future." });
        }

        static string? Clamp(string? s, int max)
        {
            var trimmed = s?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            return trimmed.Length > max ? trimmed[..max] : trimmed;
        }

        var city   = Clamp(req.City, 100)!;
        var toCity = Clamp(req.ToCity, 100);

        // What the visitor actually submitted, validated against the STORAGE
        // catalogue (case-insensitive) so a retired slug is recognised rather than
        // silently dropped. Kept for the ops alert: the admin must see the request
        // as it was made, not only as we routed it.
        var submitted = (req.Categories ?? [])
            .Select(c => c?.Trim().ToLowerInvariant())
            .Where(c => c is not null && ServiceCategories.BySlug.ContainsKey(c))
            .Select(c => c!)
            .Distinct()
            .ToList();

        // packing/insurance are retained-for-data, not sold (see
        // ServiceCategories.RetainedNotSoldSlugs). Neither may produce a top-level
        // lead in its own category — there is nobody to route it to:
        //   packing   → resolved to moving; the packing intent is recorded as a
        //               marker in the Query machine summary, which the ops alert
        //               reports and ProviderOutreachComposer reads back to render a
        //               LOCALIZED packing line. Never discarded.
        //   insurance → resolved to nothing; the lead lands on Any and an admin
        //               routes it by hand rather than it being dropped.
        var packingRequested   = submitted.Contains(ServiceCategories.Packing);
        var insuranceRequested = submitted.Contains(ServiceCategories.Insurance);

        // Exactly one resolved category maps to that enum value; zero or several
        // fall back to Any — the admin routes it manually. "moving"+"packing"
        // resolves to the single category "moving", so it still fans out.
        var validCategories = submitted
            .Select(ServiceCategories.PublicAliasFor)
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct()
            .ToList();
        var category = validCategories.Count == 1
            ? ServiceCategories.BySlug[validCategories[0]]
            : DemandLeadCategory.Any;

        var lang = req.Language?.Trim().ToLowerInvariant();
        if (lang is not ("et" or "en" or "ru" or "lv" or "lt"))
            lang = "et";

        // Compact ENGLISH machine summary for the admin list view — never
        // translated labels. E.g. "concierge: moving+warehouse | Tallinn→Tartu | 2026-08-15".
        // A retired category is appended as an explicit "+packing-addon" /
        // "+insurance-asked" marker so the routing is visible and never looks like
        // the visitor asked for less than they did.
        // The markers lead the summary, so the 500-char tail clamp below can never
        // truncate them (see ServiceCategories.HasPackingAddOn for the full contract).
        var categorySummary = validCategories.Count > 0 ? string.Join('+', validCategories) : "any";
        if (packingRequested)   categorySummary += $" {ServiceCategories.PackingAddOnMarker}";
        if (insuranceRequested) categorySummary += $" {ServiceCategories.InsuranceAskedMarker}";
        // "My date is flexible" is an ANSWER, not a blank. Recorded only when no
        // date was given, since a named date makes the question moot.
        if (req.DateFlexible == true && req.NeedDate is null)
            categorySummary += $" {ServiceCategories.DateFlexibleMarker}";

        var parts = new List<string>
        {
            categorySummary,
            toCity is not null ? $"{city}→{toCity}" : city,
        };
        // JSON binds a bare "yyyy-MM-dd" to Kind=Unspecified, which Npgsql rejects
        // for timestamptz — normalize to UTC midnight (calendar-date semantics).
        var needDate = req.NeedDate is { } nd ? DateTime.SpecifyKind(nd.Date, DateTimeKind.Utc) : (DateTime?)null;
        if (needDate is { } stamped)
            parts.Add(stamped.ToString("yyyy-MM-dd"));
        var query = $"{ServiceCategories.ConciergeQueryPrefix}{string.Join(" | ", parts)}";

        // Details stays the CUSTOMER'S OWN WORDS and nothing else. It is the one
        // field ProviderOutreachComposer prints verbatim into a cold email that is
        // written in the PROVIDER's language — an English ops note in brackets in
        // the middle of an Estonian mail reads like spam and undoes the whole point
        // of that template. The retired-category signal lives in the Query marker
        // (machine summary, admin list view) and in the ops alert instead; the
        // provider sees packing as a localized fact line rendered by the composer.
        // Insurance never reaches a provider at all — "no consumer product; route
        // by hand" is an instruction to us, not to a mover.
        var details    = Clamp(req.Details, 2000);
        var email      = req.Email.Trim();
        var photoKeys  = LeadPhotoNormalizer.KeepIssuedKeys(req.PhotoKeys);

        // The SAME answers as the ones the browser already folded into Details,
        // kept structurally as well. Details keeps being written exactly as
        // before — the frontend still sends it, and nothing may regress while it
        // does — but it is a sentence in the CUSTOMER's language, so it can only
        // ever be pasted, not translated and not queried. The structured copy is
        // what lets the outreach email speak the provider's language and lets
        // the admin ask "which moves have no lift".
        //
        // Validated against the catalogue rather than trusted: this is an
        // anonymous public endpoint, and everything stored here is later
        // rendered into mail we send to real businesses.
        var scope      = ScopeQuestions.Normalize(req.Scope);

        // ── Duplicate submit ─────────────────────────────────────────────────
        // The submit button disables itself while the mutation is in flight, but
        // that is a client-side courtesy and the network is where this actually
        // happens: a double-tap on a slow phone, a retried POST, a visitor who
        // sees no immediate confirmation and presses again. Each extra lead is
        // not a harmless duplicate row — it triggers its OWN auto fan-out, so the
        // same providers are cold-emailed twice about one customer, each copy
        // carrying its own quote token, and one business can answer one request
        // with two competing prices. That is the exact failure the inbox-level
        // dedupe inside ConciergeOutreachService exists to prevent, arriving
        // through a door it cannot see.
        //
        // Matched on the WHOLE meaningful payload, not just the address: a
        // visitor who corrects their city and resubmits is making a new request
        // and must not be silently swallowed. Identical payload inside the
        // window is, by any reasonable reading, the same request twice.
        //
        // No migration and no unique index: at this volume the window query is
        // trivial, and an index would have to encode the same fingerprint to be
        // useful. Returns the same shape as a fresh submit — the customer must
        // never be told their request failed when it did not.
        var duplicateCutoff = DateTime.UtcNow - DuplicateRequestWindow;
        var recent = await db.DemandLeads
            .AsNoTracking()
            .Where(l => l.Source == "concierge"
                     && l.Email == email
                     && l.CreatedAt >= duplicateCutoff)
            .Select(l => new { l.Id, l.City, l.ToCity, l.Category, l.NeedDate, l.Details })
            .ToListAsync();

        var duplicate = recent.FirstOrDefault(l =>
            l.City == city
            && l.ToCity == toCity
            && l.Category == category
            && l.NeedDate == needDate
            && l.Details == details);

        if (duplicate is not null)
        {
            logger.LogInformation(
                "Duplicate concierge request from {Email} within {Minutes} min — returning lead {LeadId} without a second fan-out.",
                email, DuplicateRequestWindow.TotalMinutes, duplicate.Id);
            return Ok(new { ok = true });
        }

        var lead = new DemandLead
        {
            Id        = Guid.NewGuid(),
            Email     = email,
            Name      = Clamp(req.Name, 120),
            Phone     = Clamp(req.Phone, 40),
            City      = city,
            ToCity    = toCity,
            // JSON binds a bare "yyyy-MM-dd" to Kind=Unspecified, which Npgsql rejects
            // for timestamptz — normalized to UTC midnight (calendar-date semantics)
            // where it was read above.
            NeedDate  = needDate,
            Details   = details,
            Category  = category,
            Query     = query.Length > 500 ? query[..500] : query,
            Source    = "concierge",
            // Where this request came from, as the browser saw it. Source already
            // says WHICH FORM was used ("concierge" vs "routed"); this says which
            // campaign, post or search brought them to it — the difference between
            // counting requests and knowing what a request costs. Free text on
            // purpose: it is an opaque attribution string we report on, never
            // something we branch behaviour on.
            Attribution = Clamp(req.Attribution, 300),
            // Filtered to keys the upload endpoint could actually have issued.
            // Without that a caller could point a lead at any object in the
            // private bucket — signed contracts included — and read it back
            // through the quote page. See LeadPhotoNormalizer.KeepIssuedKeys.
            PhotoKeysJson = photoKeys.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(photoKeys)
                : null,
            // Normalized above; null when nothing valid was answered, so a
            // request with no scoping answers stores nothing rather than "{}".
            ScopeJson = ScopeQuestions.Serialize(scope),
            // Somebody's home. Stored so the concierge stops brokering it by
            // hand on every job — a mover could not finalise anything without a
            // round trip — and deliberately NOT exposed to a provider until the
            // customer accepts an offer: neither ProviderOutreachComposer nor
            // the public quote DTO reads these, both keep showing the city.
            FromAddress = Clamp(req.FromAddress, 300),
            ToAddress   = Clamp(req.ToAddress, 300),
            // The customer's credential for their own status page. Minted for
            // EVERY concierge request, at creation, because the gap this closes
            // is the silence between the receipt email and the offer — a
            // customer with no account cannot otherwise tell a slow success
            // from a silent failure. Same generator as the per-recipient quote
            // token: 256 bits, url-safe, unguessable, because this page shows a
            // stranger what somebody is moving and when their home will be empty.
            StatusToken = OfferToken.Generate(),
            Language  = lang,
            Status    = DemandLeadStatus.New,
            CreatedAt = DateTime.UtcNow,
        };

        // ── Automation check ──────────────────────────────────────────────────
        // Decided BEFORE the save so the verdict can be written onto the lead the
        // operator will read, but it never blocks the save itself.
        var botReason = await DetectAutomationAsync(req, email);
        if (botReason is not null)
        {
            // Written where the operator actually looks. It is a note, not a
            // status: the lead sits in the normal New queue and gets worked like
            // any other once a human has glanced at it.
            lead.AdminNotes =
                $"[auto] Held from automatic outreach — {botReason}. " +
                "Review, then contact providers from Stage 1 if this is a real customer.";
            logger.LogWarning(
                "Concierge lead {LeadId} from {Email} held from auto-outreach: {Reason}.",
                lead.Id, email, botReason);
        }

        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        // The lead is persisted — from here NOTHING may fail the customer's
        // request.

        // ── Auto-fanout ───────────────────────────────────────────────────────
        // Ask nearby providers for a price RIGHT NOW instead of waiting for an
        // admin to open the workspace. Live data said manual-only outreach was
        // the whole failure: 10 leads, 8 provider emails ever sent, one lead
        // contacted 13 h late and one never at all — while the single batch that
        // did go out got a 75 % reply rate. The customer's request must never
        // depend on someone being awake.
        //
        // A failure here must never 500 a real customer or lose the lead: it is
        // wrapped whole, logged loudly with the lead id, and reported in the ops
        // alert so the admin knows to work it by hand.
        AutoOutreachSummary fanout;
        if (botReason is not null)
        {
            fanout = AutoOutreachSummary.Skipped("automation_suspected");
        }
        else
        {
            try
            {
                fanout = await outreachService.AutoFanOutAsync(lead);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Auto-outreach failed for concierge lead {LeadId} — lead saved, NOBODY contacted.", lead.Id);
                fanout = AutoOutreachSummary.Skipped("failed");
            }
        }

        // Enrich the instant ops alert (the concierge "phone alert") with a
        // one-click deep link into the lead's workspace and how many providers we
        // could reach right now. Nearby scope = the same 25 km the admin outreach
        // step defaults to, so the count matches what they'll see.

        // The count is decorative: a failure here must never cost us the alert
        // (a saved-but-unannounced lead is the exact silent-lead failure this
        // feature exists to prevent), let alone 500 a real customer.
        int? nearbyCount = null;
        try
        {
            var matches = await ProviderCandidateFinder.SearchAsync(
                db, lead,
                new ProviderCandidateSearch(
                    Query: null, AllEstonia: false, AllCategories: false, RadiusKm: 25, Limit: 50));
            nearbyCount = matches.Total;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Provider match count failed for concierge lead {LeadId}; alerting without it.", lead.Id);
        }

        var appUrl    = string.IsNullOrWhiteSpace(config["AppUrl"]) ? "https://ruumly.eu" : config["AppUrl"];
        var adminLink = FrontendUrl.Localized(appUrl, "et", $"admin?tab=leads&lead={lead.Id}");
        var matchLine = nearbyCount is { } count
            ? $"Matches: {count} providers within 25 km\n"
            : "";

        // Report what the visitor ASKED for, and — when a retired category was
        // rerouted (packing → moving, insurance → any) — what it was routed as. An
        // alert that only printed the routed category would quietly hide the ask.
        var askedLine = submitted.Count > 0 ? string.Join(", ", submitted) : "any";
        var routedSlug = ServiceCategories.SlugFor(lead.Category);
        if (packingRequested || insuranceRequested)
            askedLine += $" (routed as: {routedSlug})";

        try
        {
            emailQueue.EnqueueEmail(
                to:       await OpsInbox.ResolveAsync(db),
                subject:  $"New concierge request — {lead.City}",
                textBody: $"From: {lead.Name} <{lead.Email}> {lead.Phone}\n" +
                          $"Categories: {askedLine}\n" +
                          $"City: {lead.City}{(lead.ToCity is not null ? $" → {lead.ToCity}" : "")}\n" +
                          $"Date: {(lead.NeedDate?.ToString("yyyy-MM-dd") ?? "-")}\n" +
                          $"Language: {lead.Language}\n" +
                          matchLine +
                          // What the auto-fanout actually did — so the admin
                          // instantly knows whether this still needs hand-work.
                          fanout.Describe() + "\n\n" +
                          $"{lead.Details}\n\n" +
                          $"Open the workspace: {adminLink}\n" +
                          $"Work it from the admin CRM → Leads.");
        }
        catch (Exception ex)
        {
            // The lead IS saved; 500-ing the customer would lose them too and
            // still not deliver the alert. Log loudly and accept the request.
            logger.LogError(ex,
                "Ops alert enqueue failed for concierge lead {LeadId} — lead saved but UNANNOUNCED.", lead.Id);
        }

        // The customer's own receipt. Until 2026-08-13 nothing at all was sent to
        // them here: the first mail they ever got from Ruumly was the offer, days
        // later, from an address they had never corresponded with — while the
        // success screen had already promised "2-3 offers, usually within 24
        // hours". No proof it arrived, and no thread to reply to when the date
        // moved. Their address was the only channel back from them and it was
        // never once exercised.
        //
        // Same shape as the ops alert above and for the same reason: the lead is
        // already saved, so a mail failure must never turn into a 500 that tells
        // the customer their request was lost when it was not.
        try
        {
            // The services the visitor actually picked, not the single Category
            // column they collapsed into. The receipt's whole job is to read the
            // request back so they can spot their own typo, and for a
            // multi-service ask — which the intake copy explicitly invites — the
            // Category label is the generic "Service". The provider cold email
            // already recovered the real list; the customer's own receipt did
            // not. Same helper for both now (Helpers/LeadServiceLabel).
            var ack = CustomerRequestAckComposer.Compose(
                lead,
                LeadServiceLabel.For(EmailTranslations.For(lead.Language), lead),
                FrontendUrl.Contact(appUrl, lead.Language));

            emailQueue.EnqueueEmail(
                to:       lead.Email.Trim(),
                subject:  ack.Subject,
                textBody: ack.TextBody,
                // Branded HTML with the text above as fallback. It was text-only
                // while the COLD email we send to strangers was branded HTML, so
                // the customer's sole proof of receipt looked less legitimate
                // than unsolicited mail.
                htmlBody: ack.HtmlBody,
                // Reply-To ops, not noreply@: the mail explicitly invites the
                // customer to answer with a changed date or an extra detail, and
                // those replies have to land somewhere a human reads.
                replyTo:  await OpsInbox.ResolveAsync(db));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Customer acknowledgement enqueue failed for lead {LeadId} — lead saved, " +
                "customer NOT acknowledged.", lead.Id);
        }

        return Ok(new { ok = true });
    }

    /// <summary>
    /// Why this submission looks automated, or null when it looks like a person.
    ///
    /// Three independent signals, cheapest first. None of them rejects the
    /// request — see the notes on <see cref="MinFormSeconds"/> for why a
    /// suspected bot's lead is still saved and only its automatic fan-out is
    /// withheld.
    /// </summary>
    private async Task<string?> DetectAutomationAsync(ConciergeRequest req, string email)
    {
        // 1. Honeypot. A field a human never sees and therefore never fills.
        if (!string.IsNullOrWhiteSpace(req.Website))
            return "hidden field was filled in";

        // 2. Time on form. Only an explicitly implausible value counts: absent
        //    means unknown, because a service-worker-cached older bundle does not
        //    send it and its customers must keep being served normally.
        if (req.ElapsedMs is { } elapsed && elapsed >= 0 && elapsed < MinFormSeconds * 1000)
            return $"submitted {elapsed} ms after the form opened";

        // 3. Volume per address. The signal a hand-rolled POST cannot omit, and
        //    the one that actually bounds how much provider goodwill a single
        //    attacker can burn. Counts only leads that were themselves eligible
        //    for fan-out, so a held lead never pushes the next one over the line.
        var since = DateTime.UtcNow.AddDays(-1);
        var todayCount = await db.DemandLeads
            .CountAsync(l => l.Source == "concierge" && l.Email == email && l.CreatedAt >= since);
        if (todayCount >= MaxAutoFanOutPerEmailPerDay)
            return $"{todayCount} requests from this address in the last 24 h";

        return null;
    }
}
