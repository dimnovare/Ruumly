using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The lead's working draft offer — the one place a provider price lands before
/// an admin sends it. Shared by the quote page (a price the provider typed) and
/// the outreach send (a provider's standing price), so both write into the SAME
/// draft and a later real quote updates the standing-price option instead of
/// sitting beside it.
/// </summary>
public static class DraftOffers
{
    /// <summary>
    /// The lead's newest Draft, or a new tracked one. The caller saves. Callers
    /// on a relational database hold the lead row (FOR UPDATE / serializable),
    /// which is what keeps two concurrent writers from minting two drafts.
    /// </summary>
    public static async Task<Offer> FindOrCreateAsync(
        RuumlyDbContext db, DemandLead lead, string createdBy, CancellationToken ct = default)
    {
        var offer = await db.Offers
            .Include(o => o.Options)
            .Where(o => o.DemandLeadId == lead.Id && o.Status == OfferStatus.Draft)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct);
        if (offer is not null) return offer;

        offer = new Offer
        {
            Id           = Guid.NewGuid(),
            DemandLeadId = lead.Id,
            Token        = OfferToken.Generate(),
            Status       = OfferStatus.Draft,
            Language     = lead.Language is "et" or "en" or "ru" or "lv" or "lt" ? lead.Language : "et",
            CreatedAt    = DateTime.UtcNow,
            // Authorship only (no admin is behind this draft); the per-option
            // "from provider quote" badge comes from CreatedFromOutreachId.
            CreatedBy    = createdBy,
        };
        db.Offers.Add(offer);
        return offer;
    }

    /// <summary>The option title a provider-sourced option carries.</summary>
    public static string OptionTitle(string supplierName, DemandLead lead)
    {
        var title = $"{supplierName} — {lead.City}".Trim();
        return title.Length > 200 ? title[..200] : title;
    }
}
