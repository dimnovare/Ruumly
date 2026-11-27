using System.Globalization;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// A provider's own published price, used as their quote for a request until
/// they say otherwise (founder decision 2026-11-27).
///
/// Pärnu Autorent quoted the same "80 € / päev" on all seven requests it ever
/// answered, and had already put exactly that price on its claimed profile —
/// yet every new request still waited for Rainer to type it again before the
/// customer could be offered anything. Now the request reaching such a provider
/// seeds the lead's draft offer with that price straight away, and the outreach
/// email tells the provider so: update it on the quote page if it is different
/// this time, otherwise the profile price is used. Nothing reaches the customer
/// without an admin pressing send (or the default-off auto-send rule).
///
/// Deliberately narrow. Supplier.PriceFrom is a "from" price, and for most
/// services a from-price is not a quote for a specific job — a mover's "from
/// 25 €", a cleaner's "0.50 €/m²" or a storage yard's cheapest box says nothing
/// about THIS customer's move, flat or unit. Only a flat DAILY rental rate is the
/// same number whatever the job, so only van and trailer rental qualify, and
/// only when the unit is a recognised per-day unit. And only a price the
/// provider set themselves: the claim flow is its sole writer, so a profile
/// nobody has claimed never quotes anyone.
/// </summary>
public static class StandingPrice
{
    public sealed record Quote(decimal Amount, string Unit, string? Note);

    public static Quote? For(Supplier supplier, DemandLead lead)
    {
        if (lead.Category is not (DemandLeadCategory.VanRental or DemandLeadCategory.Trailer)) return null;
        if (string.IsNullOrWhiteSpace(supplier.ClaimedByEmail)) return null;
        if (supplier.PriceFrom is not { } amount || amount <= 0) return null;
        if (!PriceUnitNormalizer.IsPerDay(supplier.PriceUnit)) return null;

        var note = string.IsNullOrWhiteSpace(supplier.PriceNote) ? null : supplier.PriceNote.Trim();
        return new Quote(amount, supplier.PriceUnit!.Trim(), note);
    }

    /// <summary>"80 €/ööpäev" in the given language.</summary>
    public static string Format(Quote quote, string language) =>
        quote.Amount.ToString("0.##", CultureInfo.InvariantCulture) + " €"
        + PriceUnitNormalizer.ToCustomerLanguage(quote.Unit, language);
}
