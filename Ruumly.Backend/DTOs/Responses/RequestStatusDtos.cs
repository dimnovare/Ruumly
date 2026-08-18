namespace Ruumly.Backend.DTOs.Responses;

/// <summary>
/// The customer's own request, read back to them.
///
/// Exactly what they typed into the intake — no enrichment, no ops shorthand.
/// The point is that they can catch their OWN mistake ("I wrote Tartu, I meant
/// Tapa") while it is still cheap to fix, days before an offer lands.
///
/// Deliberately WITHOUT the street addresses, even though they are the
/// customer's own and the most consequential thing to mistype. The credential
/// for this page is a link in an email, and links in email get forwarded,
/// screenshotted and left open on shared laptops. City, route and date already
/// say enough for a person to recognise their request; the door number does
/// not need to be on a page that a stranger might end up holding.
/// </summary>
public sealed record RequestStatusRequestDto(
    /// <summary>Service slug ("moving", "warehouse", … or "any" for a multi-service ask).</summary>
    string Service,
    string City,
    string? ToCity,
    DateTime? NeedDate,
    /// <summary>
    /// The request description. Customer-authored; an admin may CORRECT it from
    /// the CRM (that is an edit to the request itself, not an internal note),
    /// and showing the corrected text is the point — it is how the customer
    /// finds out we understood them.
    /// </summary>
    string? Details,
    /// <summary>How many photos we hold, so an upload from a phone can be confirmed.</summary>
    int PhotoCount,
    DateTime SubmittedAt);

/// <summary>
/// GET /api/request-status/{token} — everything the concierge customer is
/// allowed to know about their own request while it is in flight.
///
/// WHAT IS ABSENT IS THE DESIGN. No supplier name, no supplier email, no
/// supplier count broken down by who, no price, no admin note, no raw lead
/// status. The concierge model is that the platform brokers the introduction:
/// a list of who was contacted is a list to go around us with, and a price
/// before the offer is released pre-empts a decision that is deliberately a
/// human one (offerAutoSend is off by default).
///
/// It also promises NOTHING about timing. There is no "within 24 hours" claim
/// anywhere in this funnel any more, because it could not be enforced; this
/// endpoint reports what has happened and stops there.
/// </summary>
public sealed record RequestStatusDto(
    /// <summary>
    /// Customer-facing stage, one of:
    /// <c>received</c>   — logged, no provider contacted yet;
    /// <c>contacted</c>  — providers have the request, nobody has priced it yet;
    /// <c>collecting</c> — at least one provider has come back with a price;
    /// <c>offer_sent</c> — an offer is live and waiting on the customer;
    /// <c>chosen</c>     — the customer picked an option, ops is confirming it;
    /// <c>booked</c>     — done;
    /// <c>no_match</c>   — nobody could take it (an ANSWER, not a silence);
    /// <c>closed</c>     — the request was ended for some other reason.
    ///
    /// A deliberate vocabulary of its own, NOT <c>DemandLeadStatus.ToString()</c>:
    /// the internal names ("Unmatched", "Dismissed", "Converted") are ops words
    /// that mean something slightly different to the team than to the person
    /// waiting, and wiring the wire format to an internal enum means the next
    /// person to append a member publishes it by accident.
    /// </summary>
    string State,
    RequestStatusRequestDto Request,
    /// <summary>
    /// How many providers actually received this request.
    ///
    /// Counts outreach that LEFT and was not rejected by the receiving server.
    /// A bounced address never reached a human, and telling somebody "we
    /// contacted 18 providers" when three of those messages bounced is the kind
    /// of number that reads as effort and is not. Silence from a real inbox
    /// still counts — that is a provider who was asked and did not answer,
    /// which is a true and useful thing for the customer to know.
    /// </summary>
    int ProvidersContacted,
    /// <summary>When the first of those went out. Null until any have.</summary>
    DateTime? ProvidersContactedAt,
    /// <summary>
    /// True once a real offer is on its way to (or already with) the customer.
    /// Never true for a Draft: a draft is ops working, not a promise.
    /// </summary>
    bool OfferSent,
    DateTime? OfferSentAt,
    /// <summary>
    /// The token for the customer's existing /offer/{token} page, or null.
    ///
    /// A token rather than a URL because the offer page lives under a language
    /// prefix the FRONTEND owns (/{lang}/offer/{token}); a URL built here would
    /// hard-code a language guess into an API response and send a Russian
    /// speaker to the Estonian copy of their own offer.
    /// </summary>
    string? OfferToken,
    /// <summary>
    /// True when nothing further will happen on its own — the request has ended
    /// (booked, unmatched or closed). Lets the page stop implying that someone
    /// is still working on it.
    /// </summary>
    bool Closed);
