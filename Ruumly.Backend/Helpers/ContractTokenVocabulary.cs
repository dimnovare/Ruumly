using System.Globalization;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The contract-template token cheat-sheet (design spec §2) — the single source of truth
/// for (a) which <c>{{tokens}}</c> a partner may use, and (b) how each maps to real
/// Booking/Listing/Supplier data. Used by the provider portal (vocabulary + validation)
/// and by the signing-initiate flow (filling the docx).
///
/// Formatting: dates as <c>dd.MM.yyyy</c>, money as <c>N2 €</c> (e.g. <c>49.00 €</c>).
/// A missing/empty source resolves to the empty string — the document never crashes and
/// never leaks an unresolved placeholder.
/// </summary>
public static class ContractTokenVocabulary
{
    /// <summary>A token plus a short human label, for the provider UI cheat-sheet.</summary>
    public record TokenInfo(string Token, string Label);

    /// <summary>The complete recognized vocabulary, in cheat-sheet order.</summary>
    public static readonly IReadOnlyList<TokenInfo> Vocabulary = new List<TokenInfo>
    {
        new("tenant_name",      "Tenant full name"),
        new("tenant_id_code",   "Tenant personal/ID code"),
        new("tenant_email",     "Tenant email"),
        new("tenant_phone",     "Tenant phone"),
        new("listing_name",     "Storage unit name"),
        new("listing_size",     "Storage unit size"),
        new("listing_address",  "Storage address"),
        new("listing_city",     "Storage city"),
        new("start_date",       "Rental start date"),
        new("end_date",         "Rental end date"),
        new("monthly_price",    "Monthly price"),
        new("deposit",          "Deposit"),
        new("total_price",      "Total price"),
        new("supplier_name",    "Provider company name"),
        new("supplier_reg_code","Provider registry code"),
        new("contract_number",  "Contract number (generated)"),
        new("today",            "Today's date (generated)"),
        new("payment_condition_clause", "Conditional-on-payment clause (generated, sign-then-pay)"),
    };

    /// <summary>
    /// The sign-then-pay "conditional on payment" clause. A signed-but-unpaid rental binds
    /// no one: if the deposit and first month are not paid within the window, the agreement
    /// auto-voids. Storage-only wording. The window comes from PlatformSettings
    /// (<c>booking.signedUnpaidExpiryHours</c>); 24 is the fallback default.
    /// </summary>
    public static string PaymentConditionClause(int expiryHours) =>
        $"This storage rental agreement is conditional upon payment of the deposit and first " +
        $"month's rent within {expiryHours} hours of signing. If payment is not received within " +
        $"that period, this agreement is automatically void and neither party is bound by it, and " +
        $"no payment is due.";

    private static readonly HashSet<string> Known =
        Vocabulary.Select(v => v.Token).ToHashSet(StringComparer.Ordinal);

    /// <summary>The core tokens whose absence is a soft warning (spec §3).</summary>
    public static readonly IReadOnlyList<string> CoreTokens =
        new[] { "tenant_name", "listing_name", "supplier_name", "today" };

    public static bool IsKnown(string token) => Known.Contains(token);

    /// <summary>Tokens in <paramref name="tokens"/> that are not in the vocabulary.</summary>
    public static IReadOnlyList<string> UnknownTokens(IEnumerable<string> tokens) =>
        tokens.Where(t => !Known.Contains(t)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Core tokens that are missing from <paramref name="tokens"/> (soft warning).</summary>
    public static IReadOnlyList<string> MissingCoreTokens(IEnumerable<string> tokens)
    {
        var present = tokens.ToHashSet(StringComparer.Ordinal);
        return CoreTokens.Where(c => !present.Contains(c)).ToList();
    }

    /// <summary>
    /// Builds the token→value map for a real booking. The booking must be loaded with its
    /// User, Listing (+ Location) and Supplier. Optional signer overrides (from the
    /// initiate request) take precedence for identity fields.
    /// </summary>
    public static Dictionary<string, string> BuildValues(
        Booking booking,
        string? signerName = null,
        string? signerIdCode = null,
        string? signerEmail = null,
        string? contractNumber = null,
        int paymentConditionHours = 24)
    {
        var listing  = booking.Listing;
        var user     = booking.User;
        var supplier = booking.Supplier;

        var address = listing?.Location?.Address ?? listing?.Address ?? "";
        var city    = listing?.Location?.City    ?? listing?.City    ?? "";

        var size = listing?.SizeM2 is { } m2 && m2 > 0
            ? $"{m2.ToString("0.##", CultureInfo.InvariantCulture)} m²"
            : "";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant_name"]       = !string.IsNullOrWhiteSpace(signerName) ? signerName!
                                    : (booking.ContactName ?? user?.Name ?? ""),
            ["tenant_id_code"]    = signerIdCode ?? "",
            ["tenant_email"]      = !string.IsNullOrWhiteSpace(signerEmail) ? signerEmail!
                                    : (booking.ContactEmail ?? user?.Email ?? ""),
            ["tenant_phone"]      = booking.ContactPhone ?? user?.Phone ?? "",
            ["listing_name"]      = listing?.Title ?? "",
            ["listing_size"]      = size,
            ["listing_address"]   = address,
            ["listing_city"]      = city,
            ["start_date"]        = booking.StartDate == default ? "" : booking.StartDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["end_date"]          = booking.EndDate is { } end ? end.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "",
            ["monthly_price"]     = Money(listing?.PriceFrom ?? booking.BasePrice),
            ["deposit"]           = "",                       // no deposit field in the model yet
            ["total_price"]       = Money(booking.Total),
            ["supplier_name"]     = supplier?.Name ?? "",
            ["supplier_reg_code"] = supplier?.RegistryCode ?? "",
            ["contract_number"]   = contractNumber ?? DefaultContractNumber(booking),
            ["today"]             = DateTime.UtcNow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["payment_condition_clause"] = PaymentConditionClause(paymentConditionHours),
        };
    }

    /// <summary>Sample values for the provider preview (no real booking needed).</summary>
    public static Dictionary<string, string> SampleValues() => new(StringComparer.Ordinal)
    {
        ["tenant_name"]       = "Mari Maasikas",
        ["tenant_id_code"]    = "48001011234",
        ["tenant_email"]      = "mari@example.com",
        ["tenant_phone"]      = "+372 5123 4567",
        ["listing_name"]      = "Ladu 12B",
        ["listing_size"]      = "8 m²",
        ["listing_address"]   = "Pärnu mnt 158",
        ["listing_city"]      = "Tallinn",
        ["start_date"]        = DateTime.UtcNow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        ["end_date"]          = DateTime.UtcNow.AddMonths(6).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        ["monthly_price"]     = Money(49m),
        ["deposit"]           = Money(49m),
        ["total_price"]       = Money(294m),
        ["supplier_name"]     = "Näidis Ladu OÜ",
        ["supplier_reg_code"] = "12345678",
        ["contract_number"]   = "RUUMLY-SAMPLE-0001",
        ["today"]             = DateTime.UtcNow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        ["payment_condition_clause"] = PaymentConditionClause(24),
    };

    private static string Money(decimal amount) =>
        $"{amount.ToString("N2", CultureInfo.InvariantCulture)} €";

    private static string DefaultContractNumber(Booking booking) =>
        $"RUUMLY-{booking.Id.ToString("N")[..8].ToUpperInvariant()}";
}
