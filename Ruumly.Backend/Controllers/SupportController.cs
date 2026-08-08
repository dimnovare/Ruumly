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
    /// Public contact form. Emails the team the visitor's message.
    /// Delivery is queued so transient provider failures are retried without
    /// delaying or failing the visitor's request.
    /// </summary>
    [HttpPost("contact")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Contact([FromBody] ContactRequest req)
    {
        // Resolve the team inbox from PlatformSettings; fall back to the
        // public contact address used elsewhere in the app.
        var teamEmail = await db.PlatformSettings
            .Where(s => s.Key == "siteEmail")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(teamEmail))
            teamEmail = "info@ruumly.eu";

        var lang = string.IsNullOrWhiteSpace(req.Language) ? "et" : req.Language;

        emailQueue.EnqueueEmail(
            to:       teamEmail,
            subject:  $"[Ruumly contact] {req.Subject}",
            textBody: $"From: {req.Name} <{req.Email}>\nLang: {lang}\n\n{req.Message}\n\n— Reply directly to {req.Email}");

        return Ok(new { success = true });
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

        var parts = new List<string>
        {
            categorySummary,
            toCity is not null ? $"{city}→{toCity}" : city,
        };
        if (req.NeedDate is { } needDate)
            parts.Add(needDate.ToString("yyyy-MM-dd"));
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
        var details = Clamp(req.Details, 2000);

        var lead = new DemandLead
        {
            Id        = Guid.NewGuid(),
            Email     = req.Email.Trim(),
            Name      = Clamp(req.Name, 120),
            Phone     = Clamp(req.Phone, 40),
            City      = city,
            ToCity    = toCity,
            // JSON binds a bare "yyyy-MM-dd" to Kind=Unspecified, which Npgsql rejects
            // for timestamptz — normalize to UTC midnight (calendar-date semantics).
            NeedDate  = req.NeedDate is { } nd ? DateTime.SpecifyKind(nd.Date, DateTimeKind.Utc) : null,
            Details   = details,
            Category  = category,
            Query     = query.Length > 500 ? query[..500] : query,
            Source    = "concierge",
            Language  = lang,
            Status    = DemandLeadStatus.New,
            CreatedAt = DateTime.UtcNow,
        };

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

        return Ok(new { ok = true });
    }
}
