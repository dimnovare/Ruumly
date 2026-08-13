namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// "Send me the link" — the visitor's own business email. The response is
/// identical whether or not it matches the stored contact address, so this
/// endpoint can never be used to test whether an address is on file.
/// </summary>
/// <param name="Email">The address the provider says is theirs.</param>
/// <param name="Language">UI language (et/en/ru/lv/lt) — picks the email language.</param>
public record ClaimRequestLinkRequest(string? Email, string? Language = null);

/// <summary>Redeems the emailed magic link. Single-use.</summary>
public record ClaimVerifyRequest(string? Token);

/// <summary>
/// The whole editable surface of a claimed directory profile. Deliberately
/// small: name, how to reach them, where they are, what they do, one line about
/// themselves. Everything else on a Supplier row (tier, verification badge,
/// commerce flags, publish state, slug) stays admin-only — a claim proves
/// control of an inbox, not entitlement to a badge.
///
/// There is no supplier id anywhere in this payload ON PURPOSE: the target is
/// resolved from the claim session, so a session can only ever write to the one
/// supplier it was minted for.
/// </summary>
public record ClaimUpdateProfileRequest(
    string? Name,
    string? ContactPhone,
    string? ContactEmail,
    string? WebsiteUrl,
    string? Address,
    List<string>? ServiceTypes,
    string? Description,
    // The whole reason the claim form is worth filling in. Everything above this
    // line the provider could have left to us; a price is the one thing only they
    // can give, and it is what lets us answer a customer instead of relaying a
    // question. Null = leave unchanged; an empty string clears the field.
    decimal? PriceFrom = null,
    string? PriceUnit = null,
    string? PriceNote = null);
