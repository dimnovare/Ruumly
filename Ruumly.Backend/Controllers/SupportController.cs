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
    IConfiguration config) : ControllerBase
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

        // Parse the requested categories (any ServiceCategories slug — warehouse,
        // moving, trailer, cleaning, packing, vanrental, insurance — case-
        // insensitive). Exactly one valid category maps to that enum value;
        // zero or several fall back to Any — the admin routes it manually.
        var validCategories = (req.Categories ?? [])
            .Select(c => c?.Trim().ToLowerInvariant())
            .Where(c => c is not null && ServiceCategories.BySlug.ContainsKey(c))
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
        var parts = new List<string>
        {
            validCategories.Count > 0 ? string.Join('+', validCategories) : "any",
            toCity is not null ? $"{city}→{toCity}" : city,
        };
        if (req.NeedDate is { } needDate)
            parts.Add(needDate.ToString("yyyy-MM-dd"));
        var query = $"concierge: {string.Join(" | ", parts)}";

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
            Details   = Clamp(req.Details, 2000),
            Category  = category,
            Query     = query.Length > 500 ? query[..500] : query,
            Source    = "concierge",
            Language  = lang,
            Status    = DemandLeadStatus.New,
            CreatedAt = DateTime.UtcNow,
        };

        db.DemandLeads.Add(lead);
        await db.SaveChangesAsync();

        // Enrich the instant ops alert (the concierge "phone alert"): how many
        // providers we could reach right now, and a one-click deep link into the
        // lead's workspace. Nearby scope = same 25 km radius the admin outreach
        // step defaults to, so the count matches what they'll actually see.
        var matches = await ProviderCandidateFinder.SearchAsync(
            db, lead,
            new ProviderCandidateSearch(
                Query: null, AllEstonia: false, AllCategories: false, RadiusKm: 25, Limit: 50));
        var appUrl    = string.IsNullOrWhiteSpace(config["AppUrl"]) ? "https://ruumly.eu" : config["AppUrl"];
        var adminLink = FrontendUrl.Localized(appUrl, "et", $"admin?tab=leads&lead={lead.Id}");

        emailQueue.EnqueueEmail(
            to:       await OpsInbox.ResolveAsync(db),
            subject:  $"New concierge request — {lead.City}",
            textBody: $"From: {lead.Name} <{lead.Email}> {lead.Phone}\n" +
                      $"Categories: {(validCategories.Count > 0 ? string.Join(", ", validCategories) : "any")}\n" +
                      $"City: {lead.City}{(lead.ToCity is not null ? $" → {lead.ToCity}" : "")}\n" +
                      $"Date: {(lead.NeedDate?.ToString("yyyy-MM-dd") ?? "-")}\n" +
                      $"Language: {lead.Language}\n" +
                      $"Matches: {matches.Total} providers within 25 km\n\n" +
                      $"{lead.Details}\n\n" +
                      $"Open the workspace: {adminLink}\n" +
                      $"Work it from the admin CRM → Leads.");

        return Ok(new { ok = true });
    }
}
