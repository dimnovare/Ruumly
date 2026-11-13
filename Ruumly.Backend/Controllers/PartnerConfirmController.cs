using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// The page behind the three buttons of the partner-confirmation e-mail.
///
/// GET only RENDERS a confirm page and changes nothing: corporate mail
/// scanners (Outlook Safe Links, Gmail, Mimecast) open every link in a message
/// before a human does, so a GET that recorded the answer would "answer" for
/// thousands of providers who never clicked. The answer is recorded by the
/// POST the provider's own click on "Confirm" sends.
///
/// Served by the API as plain HTML so it needs no frontend release. The token
/// is the only credential; an unknown token gets the same page as a revoked one.
/// </summary>
[ApiController]
[Route("api/partner-confirm")]
public class PartnerConfirmController(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    ILogger<PartnerConfirmController> logger) : ControllerBase
{
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> Show(string token, [FromQuery] string? a, CancellationToken ct)
    {
        var supplier = await Find(token, ct);
        if (supplier is null) return Page(PartnerConfirm.Texts.For("en"), "en", null, null, notFound: true);

        var lang = ProviderOutreachComposer.LanguageFor(supplier);
        var t = PartnerConfirm.Texts.For(lang);
        var answer = PartnerConfirm.Normalize(a);
        return Page(t, lang, supplier, answer.Length > 0 ? answer : PartnerConfirm.Yes, notFound: false, token: token);
    }

    [HttpPost("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("provider-quote")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Confirm(string token, [FromForm] string? a, CancellationToken ct)
    {
        var supplier = await Find(token, ct);
        if (supplier is null) return Page(PartnerConfirm.Texts.For("en"), "en", null, null, notFound: true);

        var answer = PartnerConfirm.Normalize(a);
        var lang = ProviderOutreachComposer.LanguageFor(supplier);
        var t = PartnerConfirm.Texts.For(lang);
        if (answer.Length == 0)
            return Page(t, lang, supplier, PartnerConfirm.Yes, notFound: false, token: token);

        var now = DateTime.UtcNow;
        var changed = await PartnerConfirm.RecordAsync(db, supplier, answer, "provider (e-mail button)", now, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Partner confirm: {Supplier} answered {Answer} ({Rows} row(s)).",
            supplier.Name, answer, changed.Count);

        // Ops sees every answer without polling the admin.
        try
        {
            var ops = await OpsInbox.ResolveAsync(db);
            emailQueue.EnqueueEmail(ops,
                $"Partner answered {answer.ToUpperInvariant()}: {supplier.Name}",
                $"{supplier.Name} ({supplier.Country}, {supplier.ContactEmail}) answered \"{answer}\" " +
                $"on the partner-confirm page.\nRows updated: {string.Join(", ", changed.Select(s => s.Name))}.\n");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Partner confirm: ops notification failed for {Supplier}.", supplier.Name);
        }

        return Done(t, lang, answer);
    }

    private Task<Supplier?> Find(string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 64
            ? Task.FromResult<Supplier?>(null)
            : db.Suppliers.FirstOrDefaultAsync(s => s.PartnerConfirmToken == token, ct);

    // ─── HTML ───────────────────────────────────────────────────────────────

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Shell(string lang, string title, string inner) =>
        $"<!doctype html><html lang=\"{lang}\"><head><meta charset=\"utf-8\">" +
        "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"robots\" content=\"noindex\">" +
        $"<title>{E(title)}</title><style>" +
        "body{margin:0;background:#ECEFF1;font-family:Arial,Helvetica,sans-serif;color:#455A64}" +
        ".card{max-width:520px;margin:32px auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #E0E0E0}" +
        ".head{background:#00897B;color:#fff;padding:18px 24px;font-size:22px;font-weight:700}" +
        ".head span{font-size:13px;font-weight:400;opacity:.9}.body{padding:24px;font-size:15px;line-height:1.6}" +
        "button{width:100%;border-radius:6px;padding:12px;font-size:15px;font-weight:700;cursor:pointer;margin:0 0 10px;" +
        "border:2px solid #00897B}.primary{background:#00897B;color:#fff}.alt{background:#fff;color:#00897B}" +
        "@media(max-width:560px){.card{margin:0;border-radius:0}}" +
        $"</style></head><body><div class=\"card\">{inner}</div></body></html>";

    private ContentResult Page(PartnerConfirm.Texts t, string lang, Supplier? s, string? answer, bool notFound,
        string? token = null)
    {
        var head = $"<div class=\"head\">Ruumly <span>&nbsp;|&nbsp; {E(t.Tag)}</span></div>";
        if (notFound || s is null || token is null)
            return Html(Shell(lang, t.PageTitle, head + $"<div class=\"body\"><p>{E(t.PageNotFound)}</p></div>"), 404);

        string Form(string a, bool primary, string label) =>
            $"<form method=\"post\" action=\"/api/partner-confirm/{Uri.EscapeDataString(token)}\" style=\"margin:0\">" +
            $"<input type=\"hidden\" name=\"a\" value=\"{a}\">" +
            $"<button type=\"submit\" class=\"{(primary ? "primary" : "alt")}\">{E(label)}</button></form>";

        var brand = PartnerConfirm.BrandName(s.Name);
        var others = PartnerConfirm.Answers.Where(x => x != answer).Select(x => Form(x, false, t.Label(x)));
        var inner = head + "<div class=\"body\">" +
                    $"<p>{E(string.Format(t.PageConfirm, brand))}</p>" +
                    Form(answer!, true, $"{t.PageSubmit}: {t.Label(answer!)}") +
                    $"<p style=\"margin:18px 0 8px;color:#78909C;font-size:13px\">{E(t.PageOther)}</p>" +
                    string.Concat(others) + "</div>";
        return Html(Shell(lang, t.PageTitle, inner), 200);
    }

    private ContentResult Done(PartnerConfirm.Texts t, string lang, string answer)
    {
        var detail = answer switch
        {
            PartnerConfirm.Yes => t.PageDoneYes,
            PartnerConfirm.Business => t.PageDoneBusiness,
            _ => t.PageDoneNo,
        };
        var inner = $"<div class=\"head\">Ruumly <span>&nbsp;|&nbsp; {E(t.Tag)}</span></div>" +
                    $"<div class=\"body\"><p><b>{E(t.PageDone)}</b></p><p>{E(detail)}</p>" +
                    "<p><a href=\"https://ruumly.eu\" style=\"color:#00897B\">ruumly.eu</a> · info@ruumly.eu</p></div>";
        return Html(Shell(lang, t.PageTitle, inner), 200);
    }

    private static ContentResult Html(string body, int status) =>
        new() { Content = body, ContentType = "text/html; charset=utf-8", StatusCode = status };
}
