using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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
    INotificationService notificationService) : ControllerBase
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
            to:       "admin@ruumly.eu",
            subject:  $"New routed quote lead — {listing.Title}",
            textBody: $"Supplier: {listing.Supplier?.Name}\nFrom: {lead.Name} <{lead.Email}> {lead.Phone}\nCity: {city}\nCategory: {lead.Category}\n\n{req.Message}");

        return Ok(new { success = true });
    }
}
