using System.Text.Json;

namespace Ruumly.Backend.DTOs.Requests;

public record ContactRequest(
    string Name,
    string Email,
    string Subject,
    string Message,
    string? Language = null,
    /// <summary>
    /// Set only when the message was written on a partner's public page
    /// (/{lang}/partner/{slug}) — the same slug <c>GET /api/suppliers/by-slug/{slug}</c>
    /// resolves. It turns the message into a routed <c>DemandLead</c> with
    /// <c>Source = "partner-page"</c> aimed at that partner instead of an
    /// untracked note in the ops inbox.
    ///
    /// OPTIONAL, and it must stay that way: the plain /contact page posts without
    /// it, as does any older bundle still served from the service-worker cache,
    /// and a slug that no longer resolves degrades to exactly the old behaviour
    /// rather than failing the visitor.
    /// </summary>
    string? PartnerSlug = null
);

/// <summary>
/// Public "request a quote" from a listing detail page (moving/trailer).
/// Routed to the listing's partner as a <c>DemandLead</c>.
/// </summary>
public record QuoteLeadRequest(
    Guid ListingId,
    string Email,
    string? Name = null,
    string? Phone = null,
    string? City = null,
    string? Message = null,
    string? Language = null
);

/// <summary>
/// Public concierge intake ("tell us what you need, we find the partner").
/// Not tied to any listing — captured as a <c>DemandLead</c> with
/// <c>Source = "concierge"</c> and worked by the admin CRM.
/// Categories accepts the consumer-selectable slugs (case-insensitive):
/// "warehouse" | "moving" | "trailer" | "cleaning" | "vanrental".
/// "packing" and "insurance" are still recognised — they are live in indexed URLs
/// and old clients — but never produce a lead in their own category: packing is
/// routed to moving as an add-on, insurance falls back to Any. See
/// Constants/ServiceCategories.RetainedNotSoldSlugs.
/// </summary>
public record ConciergeRequest(
    string Email,
    string City,
    string? Name = null,
    string? Phone = null,
    List<string>? Categories = null,
    string? ToCity = null,
    DateTime? NeedDate = null,
    string? Details = null,
    string? Language = null,
    /// <summary>
    /// Opaque first-touch marketing attribution collected by the browser (UTM
    /// parameters, click ids, external referrer, landing path). Optional, and
    /// old clients that omit it keep working. See DemandLead.Attribution.
    /// </summary>
    string? Attribution = null,
    /// <summary>
    /// HONEYPOT. Rendered off-screen and hidden from assistive technology, so a
    /// human never fills it and a form-filling bot usually does. Any value at all
    /// marks the submission as automated.
    /// </summary>
    string? Website = null,
    /// <summary>
    /// Milliseconds between the funnel opening and this submit, as the browser
    /// measured it. A real three-step form cannot be completed in a couple of
    /// seconds.
    ///
    /// Advisory and OPTIONAL: absent is treated as unknown, never as suspicious.
    /// The SPA is served through a service worker, so an old cached bundle that
    /// does not send this field must keep working exactly as before — treating
    /// its silence as bot-like would quietly stop fanning out real requests.
    /// </summary>
    long? ElapsedMs = null,
    /// <summary>
    /// The visitor explicitly answered "my date is flexible" instead of naming a
    /// day. Only meaningful when <c>NeedDate</c> is absent, and recorded as a
    /// marker on the lead's Query — see ServiceCategories.DateFlexibleMarker for
    /// why the distinction matters to the provider email.
    /// </summary>
    bool? DateFlexible = null,
    /// <summary>
    /// Private-bucket keys returned by POST /api/leads/photos. Validated
    /// server-side before storage — a caller must not be able to point a lead
    /// at an arbitrary object. See DemandLead.PhotoKeysJson.
    /// </summary>
    List<string>? PhotoKeys = null,
    /// <summary>
    /// The intake's one-tap scoping answers, as
    /// <c>{"movingSize":2,"movingAccess":3}</c> — question id → 1-based chip
    /// position. Validated against <c>Constants.ScopeQuestions</c> before
    /// storage: unknown ids and out-of-range positions are dropped, never
    /// trusted, and never 400 the request.
    ///
    /// Bound as <see cref="JsonElement"/> values rather than <c>int</c> on
    /// purpose. A <c>Dictionary&lt;string,int&gt;</c> makes ONE malformed value
    /// ("movingSize": null, from a stale service-worker-cached bundle or a
    /// hand-rolled POST) a model-binding 400 that loses the entire request —
    /// and the scoping answers are an extra on a request, never worth the
    /// request itself. See ScopeQuestions.Normalize.
    /// </summary>
    Dictionary<string, JsonElement>? Scope = null,
    /// <summary>
    /// Street address the job starts at, and ends at for a move. Optional and
    /// stored as given — we do not geocode or validate it, because the person
    /// who lives there is the authority on their own address.
    ///
    /// NEVER reaches a provider before the customer accepts an offer: it is
    /// absent from the outreach email and from the public quote DTO, which keep
    /// showing the city exactly as before. See DemandLead.FromAddress.
    /// </summary>
    string? FromAddress = null,
    string? ToAddress = null
);
