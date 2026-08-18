using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Constants;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// The concierge customer's own view of their request: /{lang}/request-status/{token}.
///
/// WHY THIS EXISTS. A concierge customer has no account. Between the receipt
/// email and an offer that may be days away they hear nothing at all, so a slow
/// success and a total failure look identical from their side. That is not
/// hypothetical: a city-matching bug once emailed nobody for weeks and nothing
/// in the product could have told a waiting customer; and on 2026-08-18 five
/// Viljandi storage requests reached 18 providers and produced zero replies,
/// with the five people who filed them left staring at a receipt.
///
/// WHAT IT REFUSES TO SAY. Never a supplier name, address or email — not one,
/// not aggregated, not "3 providers in Viljandi". The platform brokers the
/// introduction, and a list of who was contacted is a list to go around us
/// with. Never a price before the offer is released: releasing an offer is a
/// human decision (offerAutoSend is off by default) and a status page must not
/// pre-empt it. Never an admin note or a raw <see cref="DemandLeadStatus"/>.
///
/// It also promises nothing about timing. There is deliberately no "within 24
/// hours" claim left anywhere in this funnel because it could not be enforced.
/// This endpoint reports what has happened, and stops.
///
/// The <see cref="DemandLead.StatusToken"/> is the only credential, and an
/// unknown token is indistinguishable from a missing one (404) — the same
/// contract the public offer and quote pages hold, for the same reason: the
/// difference between "no such request" and "a request you may not see" is
/// itself information about somebody else.
/// </summary>
[ApiController]
[Route("api/request-status")]
public class RequestStatusController(RuumlyDbContext db) : ControllerBase
{
    /// <summary>
    /// Lead states in which nothing further will happen on its own. Mirrors
    /// QuoteController's list — the two surfaces must agree on what "closed"
    /// means, or a customer is told their request is live while the provider
    /// looking at the same request is told it is over.
    /// </summary>
    private static readonly DemandLeadStatus[] TerminalLeadStatuses =
    [
        DemandLeadStatus.Converted,
        DemandLeadStatus.Dismissed,
        DemandLeadStatus.Unmatched,
    ];

    /// <summary>
    /// Outreach that never reached a human. Excluded from the contacted count:
    /// a bounced address was not a conversation, and counting it would inflate
    /// the one number on this page whose entire value is that it is honest.
    ///
    /// Complained is NOT here — the recipient marked it as spam, which means it
    /// arrived and was read enough to be judged.
    /// </summary>
    private static readonly ProviderOutreachStatus[] NeverArrivedStatuses =
    [
        ProviderOutreachStatus.Bounced,
    ];

    /// <summary>
    /// The customer's status page payload. Read-only, sends no email, creates
    /// nothing — unlike the offer page it does not even record a view, because
    /// this page is meant to be refreshed by an anxious person and a read
    /// receipt that fires on every refresh is not a fact worth storing.
    /// </summary>
    // "search" bucket: read-only and email-free, exactly like the public offer
    // and quote GETs. It must never share a bucket with anything that sends
    // mail — a customer refreshing this page must not be able to exhaust the
    // permits their own /leads/request submit needs from the same IP.
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(string token, CancellationToken ct)
    {
        // Blank tokens short-circuit BEFORE the query: `StatusToken == ""` would
        // otherwise be a real lookup, and every legacy row carries null anyway.
        if (string.IsNullOrWhiteSpace(token))
            return NotFound(new { error = "Request not found." });

        var lead = await db.DemandLeads
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.StatusToken == token, ct);
        if (lead is null)
            return NotFound(new { error = "Request not found." });

        // Projected to the two fields this endpoint may use. Selecting whole
        // outreach rows would pull SentTo (a provider's email address) into the
        // process that is building a response for the customer, one careless
        // edit away from being serialised.
        var outreach = await db.ProviderOutreaches
            .AsNoTracking()
            .Where(o => o.DemandLeadId == lead.Id)
            .Select(o => new { o.Status, o.SentAt, o.QuotedAt })
            .ToListAsync(ct);

        var reached = outreach.Where(o => !NeverArrivedStatuses.Contains(o.Status)).ToList();
        var providersContacted = reached.Count;
        var providersContactedAt = reached.Count == 0
            ? (DateTime?)null
            : reached.Min(o => o.SentAt);

        // "A provider has priced this" — the fact, not the price. QuotedAt is
        // read rather than Status == Replied because a row can be moved between
        // statuses by hand, while the timestamp is only ever written by an
        // actual submitted quote.
        var anyQuoteBack = reached.Any(o => o.QuotedAt is not null);

        // Only offers the customer can actually open. Draft is ops still
        // working (announcing it would pre-empt the human send decision) and
        // Expired 404s on the offer page — linking to either would be a lie or
        // a dead end. Same filter as OffersController.FindLiveOffer.
        var liveOffers = await db.Offers
            .AsNoTracking()
            .Where(o => o.DemandLeadId == lead.Id
                && o.Status != OfferStatus.Draft
                && o.Status != OfferStatus.Expired)
            .Select(o => new { o.Token, o.Status, o.SentAt, o.CreatedAt })
            .ToListAsync(ct);

        // A chosen offer wins over a merely newer one: once somebody has picked
        // an option, that is the offer they mean, whatever ops has created
        // since. Ordered in memory because a lead has a handful of offers at
        // most and the alternative is a conditional ORDER BY that each provider
        // translates differently.
        var offer = liveOffers
            .OrderBy(o => o.Status == OfferStatus.Chosen ? 0 : 1)
            .ThenByDescending(o => o.SentAt ?? o.CreatedAt)
            .FirstOrDefault();

        var closed = TerminalLeadStatuses.Contains(lead.Status);

        return Ok(new RequestStatusDto(
            State: ResolveState(lead.Status, offer?.Status, anyQuoteBack, providersContacted),
            Request: new RequestStatusRequestDto(
                Service:     ServiceCategories.SlugFor(lead.Category),
                City:        lead.City,
                ToCity:      lead.ToCity,
                NeedDate:    lead.NeedDate,
                Details:     lead.Details,
                PhotoCount:  LeadPhotos.Count(lead.PhotoKeysJson),
                SubmittedAt: lead.CreatedAt),
            ProvidersContacted:   providersContacted,
            ProvidersContactedAt: providersContactedAt,
            OfferSent:            offer is not null,
            OfferSentAt:          offer?.SentAt,
            OfferToken:           offer?.Token,
            Closed:               closed));
    }

    /// <summary>
    /// Maps the internal lifecycle onto the vocabulary the customer reads.
    ///
    /// The lead's own terminal statuses are checked FIRST and separately,
    /// because they are the only place this page can be genuinely useful rather
    /// than merely reassuring. "Unmatched" in particular is an answer — nobody
    /// could take this job — and a person who has been waiting a week deserves
    /// it plainly instead of an "in progress" that never ends. That is the
    /// exact failure the five silent Viljandi requests produced.
    /// </summary>
    private static string ResolveState(
        DemandLeadStatus leadStatus,
        OfferStatus? offerStatus,
        bool anyQuoteBack,
        int providersContacted) => leadStatus switch
        {
            DemandLeadStatus.Converted => "booked",
            DemandLeadStatus.Unmatched => "no_match",
            DemandLeadStatus.Dismissed => "closed",
            _ when offerStatus == OfferStatus.Chosen => "chosen",
            _ when offerStatus is not null           => "offer_sent",
            _ when anyQuoteBack                      => "collecting",
            _ when providersContacted > 0            => "contacted",
            _                                        => "received",
        };
}
