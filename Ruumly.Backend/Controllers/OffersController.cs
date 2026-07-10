using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Public side of the concierge offer loop: the customer opens
/// /offer/{token} from their email and picks an option. The 256-bit token is
/// the only credential — unknown, draft and expired tokens all look the same
/// (404) so the endpoint leaks nothing about what exists.
/// </summary>
[ApiController]
[Route("api/offers")]
public class OffersController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue) : ControllerBase
{
    private const string OpsInbox = "info@ruumly.eu";

    /// <summary>
    /// Sanitized offer for the public page. The first successful GET of a
    /// Sent offer marks it Viewed (read receipt for the admin workspace).
    /// </summary>
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOffer(string token)
    {
        var offer = await FindLiveOffer(token);
        if (offer is null) return NotFound(new { error = "Offer not found." });

        if (offer.Status == OfferStatus.Sent)
        {
            offer.Status   = OfferStatus.Viewed;
            offer.ViewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return Ok(MapPublic(offer));
    }

    /// <summary>
    /// The customer picks an option. Idempotent: re-choosing the same option
    /// returns 200 again; choosing a different one after the fact is a 409 —
    /// changing their mind goes through the ops team (email/phone).
    /// </summary>
    [HttpPost("{token}/choose")]
    [AllowAnonymous]
    [EnableRateLimiting("public-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChooseOption(string token, [FromBody] ChooseOptionRequest req)
    {
        var offer = await FindLiveOffer(token);
        if (offer is null) return NotFound(new { error = "Offer not found." });

        var option = offer.Options.FirstOrDefault(o => o.Id == req.OptionId);
        if (option is null) return BadRequest(new { error = "Unknown option." });

        if (offer.Status == OfferStatus.Chosen)
        {
            return offer.ChosenOptionId == option.Id
                ? Ok(new { ok = true, chosenOptionId = option.Id, chosenAt = offer.ChosenAt })
                : Conflict(new { error = "An option has already been chosen for this offer." });
        }

        offer.Status         = OfferStatus.Chosen;
        offer.ChosenAt       = DateTime.UtcNow;
        offer.ChosenOptionId = option.Id;
        await db.SaveChangesAsync();

        // offer_chosen_admin_notification — internal ops inbox, Estonian only
        // (composed inline like the other admin-facing ops emails).
        var lead  = offer.DemandLead;
        var price = option.PriceAmount is { } p
            ? $" — {p.ToString("0.##", CultureInfo.InvariantCulture)} €" +
              (string.IsNullOrWhiteSpace(option.PriceUnit) ? "" : $" / {option.PriceUnit}")
            : "";
        emailQueue.EnqueueEmail(
            to:      OpsInbox,
            subject: $"Ruumly — klient valis pakkumise ({lead?.City})",
            textBody:
                $"Klient valis pakkumise.\n\n" +
                $"Päring: {lead?.Query}\n" +
                $"Klient: {lead?.Name} <{lead?.Email}> {lead?.Phone}\n" +
                $"Valik: {option.Title}{price}\n" +
                (option.Supplier is { } s ? $"Partner: {s.Name} <{s.ContactEmail}>\n" : "") +
                $"Pakkumine: {offer.Id}\n\n" +
                $"Kinnita partneriga ja anna kliendile teada.");

        return Ok(new { ok = true, chosenOptionId = option.Id, chosenAt = offer.ChosenAt });
    }

    /// <summary>
    /// Resolves a token to a customer-visible offer: Draft (not yet published)
    /// and Expired (closed) are treated exactly like unknown tokens.
    /// </summary>
    private async Task<Offer?> FindLiveOffer(string token) =>
        string.IsNullOrWhiteSpace(token)
            ? null
            : await db.Offers
                .Include(o => o.Options).ThenInclude(op => op.Supplier)
                .Include(o => o.DemandLead)
                .FirstOrDefaultAsync(o => o.Token == token
                    && o.Status != OfferStatus.Draft
                    && o.Status != OfferStatus.Expired);

    /// <summary>
    /// Sanitized DTO: no token echo, no admin metadata (CreatedBy, AdminNotes),
    /// no supplier contact details, and no customer PII beyond the request
    /// facts the customer submitted themselves (it's their page).
    /// </summary>
    private static object MapPublic(Offer offer)
    {
        var lead = offer.DemandLead;
        return new
        {
            status         = offer.Status.ToString().ToLower(),
            language       = offer.Language,
            customerNote   = offer.CustomerNote,
            sentAt         = offer.SentAt,
            chosenOptionId = offer.ChosenOptionId,
            lead = lead is null ? null : new
            {
                category = ServiceCategories.SlugFor(lead.Category),
                city     = lead.City,
                toCity   = lead.ToCity,
                needDate = lead.NeedDate,
                details  = lead.Details,
            },
            options = offer.Options
                .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
                .Select(o => new
                {
                    id           = o.Id,
                    title        = o.Title,
                    priceAmount  = o.PriceAmount,
                    priceUnit    = o.PriceUnit,
                    notes        = o.Notes,
                    supplierName = o.Supplier?.Name,
                })
                .ToList(),
        };
    }
}
