using System.Text;
using System.Web;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// The PLATFORM-DEFAULT storage rental agreement used as an in-code fallback when a supplier
/// has no active contract template of their own. With sign-then-pay, signing is mandatory
/// before payment; without this fallback a non-templated supplier's listing would dead-end at
/// the sign gate and be unbookable.
///
/// <para>
/// This is intentionally NOT a database row: <see cref="Models.ContractTemplate"/> requires a
/// <c>SupplierId</c> (it is supplier-scoped), and the default belongs to no supplier. Instead it
/// is a fixed in-code body keyed by the sentinel <see cref="TemplateId"/>. Because
/// <see cref="Models.SignedContract.ContractTemplateId"/> is a plain non-nullable Guid with NO
/// foreign key, storing the sentinel id on a signed contract is safe — no migration, no FK
/// violation.
/// </para>
///
/// <para>
/// The body is single-sourced using the <see cref="ContractTokenVocabulary"/> tokens (the same
/// set the docx/Dokobit path uses), so both rendering paths fill it with
/// <see cref="ContractTokenVocabulary.BuildValues"/>.
/// </para>
///
/// IMPORTANT (LEGAL): the default agreement text below is a reasonable storage-only fallback,
/// but it has NOT been reviewed by counsel. The operator MUST have this reviewed by their legal
/// counsel before relying on it in production. Suppliers should be encouraged to upload their own
/// vetted template.
/// </summary>
public static class PlatformDefaultContract
{
    /// <summary>
    /// Fixed sentinel id identifying the platform default. Not a real <see cref="Models.ContractTemplate"/>
    /// row — used to route rendering/signing through the in-code default and to stamp
    /// <see cref="Models.SignedContract.ContractTemplateId"/>.
    /// </summary>
    public static readonly Guid TemplateId = new("d0000000-0000-0000-0000-00000000def0");

    public const string Name = "Ruumly standard storage rental agreement";

    /// <summary>
    /// The agreement body as an ordered list of paragraphs, using
    /// <see cref="ContractTokenVocabulary"/> tokens. Lines beginning with <c>#</c> are H1,
    /// <c>##</c> are H2; everything else is a body paragraph. The docx builder strips the markers
    /// (see <see cref="Paragraphs"/>); the HTML renderer maps them to heading tags.
    ///
    /// Storage-only wording. Governing law: Republic of Estonia.
    /// </summary>
    private static readonly IReadOnlyList<string> Body = new[]
    {
        "# Storage Rental Agreement",
        "Contract No. {{contract_number}}, concluded on {{today}}.",

        "## 1. Parties",
        "This storage rental agreement (the \"Agreement\") is concluded between the storage provider "
            + "{{supplier_name}} (registry code {{supplier_reg_code}}) (the \"Provider\") and the tenant "
            + "{{tenant_name}} (personal/ID code {{tenant_id_code}}, email {{tenant_email}}, phone "
            + "{{tenant_phone}}) (the \"Tenant\").",

        "## 2. The Storage Unit",
        "The Provider lets to the Tenant the storage unit \"{{listing_name}}\" with an approximate "
            + "size of {{listing_size}}, located at {{listing_address}}, {{listing_city}} (the \"Unit\"). "
            + "The Unit is rented for the storage of the Tenant's goods only.",

        "## 3. Term",
        "The rental term begins on {{start_date}} and continues until {{end_date}}, unless extended or "
            + "terminated earlier in accordance with this Agreement.",

        "## 4. Fees",
        "The rent is {{monthly_price}} per month. The total amount payable for the agreed term is "
            + "{{total_price}}. All fees are due in advance for each rental period.",

        "## 5. Conditional Upon Payment",
        "{{payment_condition_clause}}",

        "## 6. Permitted Use and Prohibited Goods",
        "The Tenant shall use the Unit solely for lawful storage and shall not store any hazardous, "
            + "flammable, explosive, perishable, living, stolen or otherwise illegal goods, nor any goods "
            + "whose storage is prohibited by law. The Tenant remains the owner of and is solely "
            + "responsible for the stored goods at all times.",

        "## 7. Access",
        "The Tenant may access the Unit during the access hours and under the access procedures "
            + "communicated by the Provider. The Provider may enter the Unit where required by law, in an "
            + "emergency, or to protect the property, giving reasonable notice where practicable.",

        "## 8. Liability",
        "The Provider provides storage space only and is not a bailee or insurer of the stored goods. "
            + "To the maximum extent permitted by law, the Provider is not liable for loss of or damage to "
            + "the stored goods, and the Tenant is advised to insure goods of value. Nothing in this "
            + "Agreement limits liability that cannot be limited under applicable law.",

        "## 9. Termination",
        "Either party may terminate this Agreement by giving notice as required by law or as otherwise "
            + "agreed in writing. Upon termination the Tenant shall remove all goods and leave the Unit "
            + "empty and clean; otherwise the Provider may handle remaining goods as permitted by law.",

        "## 10. Governing Law",
        "This Agreement is governed by the laws of the Republic of Estonia. Disputes that cannot be "
            + "resolved amicably shall be settled by the competent court of the Republic of Estonia.",

        "## 11. Signatures",
        "By signing electronically, the Tenant {{tenant_name}} confirms having read and agreed to this "
            + "Agreement on {{today}}. The electronic signature has the same legal effect as a handwritten "
            + "signature.",
    };

    /// <summary>
    /// The body lines for docx building, with the heading markers (<c>#</c> / <c>##</c>) stripped.
    /// One entry → one docx paragraph. The <c>{{token}}</c> placeholders are preserved literally so
    /// the existing <see cref="Services.Interfaces.IContractDocumentService.Fill"/> replaces them.
    /// </summary>
    public static IReadOnlyList<string> Paragraphs =>
        Body.Select(StripHeadingMarker).ToList();

    /// <summary>
    /// Renders the default agreement as a clean, self-contained styled HTML document, replacing
    /// every <c>{{token}}</c> from <paramref name="values"/> (HTML-encoding the substituted
    /// values). Used by the HTML/canvas rendering path.
    /// </summary>
    public static string RenderHtml(IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<style>");
        sb.Append("body{font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;");
        sb.Append("color:#1a1a1a;line-height:1.6;max-width:760px;margin:0 auto;padding:24px;}");
        sb.Append("h1{font-size:1.6rem;margin:0 0 .25rem;}");
        sb.Append("h2{font-size:1.05rem;margin:1.4rem 0 .35rem;border-bottom:1px solid #e5e5e5;padding-bottom:.2rem;}");
        sb.Append("p{margin:.35rem 0;}");
        sb.Append("</style></head><body>");

        foreach (var line in Body)
        {
            var (tag, text) = Classify(line);
            var filled = HttpUtility.HtmlEncode(FillTokens(text, values));
            sb.Append('<').Append(tag).Append('>').Append(filled)
              .Append("</").Append(tag).Append('>');
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Replaces <c>{{token}}</c> occurrences from <paramref name="values"/>; unknown → empty.</summary>
    private static string FillTokens(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var info in ContractTokenVocabulary.Vocabulary)
        {
            var replacement = values.TryGetValue(info.Token, out var v) ? v ?? "" : "";
            text = text.Replace("{{" + info.Token + "}}", replacement);
        }
        return text;
    }

    private static (string Tag, string Text) Classify(string line)
    {
        if (line.StartsWith("## ", StringComparison.Ordinal)) return ("h2", line[3..]);
        if (line.StartsWith("# ", StringComparison.Ordinal))  return ("h1", line[2..]);
        return ("p", line);
    }

    private static string StripHeadingMarker(string line) => Classify(line).Text;
}
