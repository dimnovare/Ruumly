using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Partner confirmation: ask an unverified directory provider, with one click,
/// whether they want Ruumly's customer requests. Three answers, each one useful:
/// <list type="bullet">
/// <item><c>yes</c> — keep sending (and a row deactivated for silence comes back).</item>
/// <item><c>business</c> — they serve companies only: <see cref="Supplier.ServesConsumers"/> = false.</item>
/// <item><c>no</c> — stop: marketing opt-out + deactivate, exactly like a REMOVE reply.</item>
/// </list>
/// Timeline (run by PartnerConfirmFollowUpJob): first mail → 14 days → one
/// reminder → 7 days → deactivate. Deactivation for silence is NOT an opt-out.
/// </summary>
public static class PartnerConfirm
{
    public const string Yes      = "yes";
    public const string Business = "business";
    public const string No       = "no";
    public static readonly string[] Answers = [Yes, Business, No];

    public const int ReminderAfterDays = 14;
    public const int ExpireAfterReminderDays = 7;

    /// <summary>PlatformSettings key; the follow-up job does nothing unless it is "on".</summary>
    public const string AutomationSetting = "partnerConfirmAutomation";
    /// <summary>PlatformSettings key: max reminder e-mails per job run (Resend daily cap).</summary>
    public const string DailyCapSetting = "partnerConfirmDailyCap";
    public const int DefaultDailyCap = 80;

    /// <summary>Where the buttons point: the API serves the confirm page itself (no SPA change).</summary>
    public const string DefaultApiUrl = "https://api.ruumly.eu";

    public static string ApiUrl(IConfiguration config) =>
        (string.IsNullOrWhiteSpace(config["PublicApiUrl"]) ? DefaultApiUrl : config["PublicApiUrl"]!).TrimEnd('/');

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Normalize(string? answer) => answer?.Trim().ToLowerInvariant() switch
    {
        "yes" or "jah" or "ja" or "taip" => Yes,
        "business" or "b2b" => Business,
        "no" or "ei" or "ne" or "nē" => No,
        _ => "",
    };

    /// <summary>The name a provider recognises: legal form, branch and parenthetical stripped.</summary>
    public static string BrandName(string name)
    {
        var n = Regex.Split(name ?? "", @"\s+[—–-]\s+")[0];
        n = Regex.Replace(n, @"\s*\(.*?\)", "");
        n = Regex.Replace(n.Trim(), @"^(OÜ|AS|SIA|UAB|IĮ|MB)\s+|,?\s+(OÜ|AS|SIA|UAB|IĮ|MB)$", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @",\s[^,]*\d.*$", "");
        return n.Trim(' ', '"', '„', '“');
    }

    /// <summary>Lower-cased inbox, so branches sharing one address are one group.</summary>
    public static string InboxKey(Supplier s) => (s.ContactEmail ?? "").Trim().ToLowerInvariant();

    // ─── Recording an answer ────────────────────────────────────────────────

    /// <summary>
    /// Applies <paramref name="answer"/> to the supplier and every PENDING or
    /// expired sibling that shares its inbox (one letter went to that inbox, so
    /// one click answers for all of it). Returns the rows changed. Caller saves.
    /// </summary>
    public static async Task<List<Supplier>> RecordAsync(
        RuumlyDbContext db, Supplier supplier, string answer, string source, DateTime now, CancellationToken ct = default)
    {
        answer = Normalize(answer);
        if (answer.Length == 0) throw new ArgumentException("answer must be yes, business or no", nameof(answer));

        var inbox = InboxKey(supplier);
        var group = inbox.Length == 0
            ? [supplier]
            : await db.Suppliers
                .Where(s => s.Id == supplier.Id
                         || (s.ContactEmail.ToLower().Trim() == inbox && s.PartnerConfirmSentAt != null))
                .ToListAsync(ct);
        if (!group.Contains(supplier)) group.Add(supplier);

        foreach (var s in group)
        {
            var previous = s.PartnerConfirmAnswer;
            s.PartnerConfirmAnswer     = answer;
            s.PartnerConfirmAnsweredAt = now;
            s.UpdatedAt                = now;

            switch (answer)
            {
                case Yes:
                    s.ServesConsumers = true;
                    // Silence is not refusal: a late yes restores a row the job retired.
                    if (s.PartnerConfirmExpiredAt is not null && s.MarketingOptOutAt is null)
                    {
                        s.IsActive = true;
                        s.PartnerConfirmExpiredAt = null;
                    }
                    // A "no" clicked by mistake and corrected on the same page is
                    // undone; an opt-out from any OTHER channel is never touched.
                    if (previous == No && s.MarketingOptOutReason == OptOutReason)
                    {
                        s.MarketingOptOutAt = null;
                        s.MarketingOptOutReason = null;
                        s.IsActive = true;
                    }
                    break;
                case Business:
                    s.ServesConsumers = false;
                    if (previous == No && s.MarketingOptOutReason == OptOutReason)
                    {
                        s.MarketingOptOutAt = null;
                        s.MarketingOptOutReason = null;
                        s.IsActive = true;
                    }
                    break;
                case No:
                    s.MarketingOptOutAt   ??= now;
                    s.MarketingOptOutReason = s.MarketingOptOutReason ?? OptOutReason;
                    s.IsActive              = false;
                    break;
            }

            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), Action = "supplier.partner_confirm_answer", Actor = source,
                Target = s.Name, CreatedAt = now,
                Detail = $"Answer: {answer} (was {previous ?? "none"}). Inbox {inbox}. " +
                         $"IsActive={s.IsActive}, ServesConsumers={s.ServesConsumers}.",
            });
        }
        return group;
    }

    public const string OptOutReason = "partner-confirm: NO";

    // ─── The e-mail ─────────────────────────────────────────────────────────

    public sealed record Message(string Language, string Subject, string TextBody, string HtmlBody);

    private const string Teal = "#00897B";

    /// <param name="requestsSent">Customer requests already forwarded to this inbox (0 = omit the line).</param>
    /// <param name="firstSentAt">Set for the reminder: the date of the first mail.</param>
    public static Message Compose(
        Supplier supplier, string token, string apiUrl, int requestsSent, DateTime? firstSentAt = null)
    {
        var lang  = ProviderOutreachComposer.LanguageFor(supplier);
        var t     = Texts.For(lang);
        var brand = BrandName(supplier.Name);
        var reminder = firstSentAt is not null;
        string Link(string a) => $"{apiUrl}/api/partner-confirm/{Uri.EscapeDataString(token)}?a={a}";
        string E(string s) => WebUtility.HtmlEncode(s);

        var subject = (reminder ? t.ReminderPrefix : "") + string.Format(t.Subject, brand);
        var sentLine = requestsSent > 0 ? string.Format(t.Sent, t.Count(requestsSent)) : null;
        var reminderLine = reminder
            ? string.Format(t.Reminder, firstSentAt!.Value.ToString("dd.MM.yyyy"), brand, ExpireAfterReminderDays)
            : null;

        var text = new StringBuilder();
        text.AppendLine(t.Hello).AppendLine();
        text.AppendLine(string.Format(t.Intro, brand)).AppendLine();
        if (reminderLine is not null) text.AppendLine(reminderLine).AppendLine();
        text.AppendLine(t.What).AppendLine();
        text.AppendLine(t.Free).AppendLine();
        if (sentLine is not null) text.AppendLine(sentLine).AppendLine();
        text.AppendLine(t.HowHeading);
        for (var i = 0; i < t.How.Length; i++) text.AppendLine($"{i + 1}. {t.How[i]}");
        text.AppendLine().AppendLine(t.Ask);
        text.AppendLine($"{t.BtnYes}: {Link(Yes)}");
        text.AppendLine($"{t.BtnBusiness}: {Link(Business)}");
        text.AppendLine($"{t.BtnNo}: {Link(No)}");
        text.AppendLine().AppendLine(t.OrReply).AppendLine();
        text.AppendLine(t.Bye).AppendLine(t.Team).AppendLine("info@ruumly.eu · ruumly.eu").AppendLine();
        text.AppendLine(Footer(t, brand, html: false));

        string Btn(string label, string href, bool primary) =>
            $"<tr><td style=\"padding:0 0 10px;\"><a href=\"{E(href)}\" style=\"display:inline-block;min-width:260px;" +
            (primary
                ? $"background:{Teal};color:#ffffff;border:2px solid {Teal};"
                : $"background:#ffffff;color:{Teal};border:2px solid {Teal};") +
            $"border-radius:6px;padding:11px 20px;text-decoration:none;font-weight:700;font-size:15px;text-align:center;\">{E(label)}</a></td></tr>";

        var p = "style=\"margin:0 0 14px;\"";
        var how = string.Concat(t.How.Select(x => $"<li style=\"margin-bottom:6px;\">{E(x)}</li>"));
        var html = new StringBuilder();
        html.Append($"<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border:1px solid #ECEFF1;border-radius:8px;font-family:Arial,Helvetica,sans-serif;\">");
        html.Append($"<tr><td style=\"background:{Teal};padding:18px 28px;border-radius:8px 8px 0 0;color:#ffffff;font-size:22px;font-weight:700;letter-spacing:0.5px;\">Ruumly<span style=\"font-size:13px;font-weight:400;opacity:0.9;\">&nbsp;&nbsp;|&nbsp;&nbsp;{E(t.Tag)}</span></td></tr>");
        html.Append("<tr><td style=\"padding:28px;color:#455A64;font-size:15px;line-height:1.6;\">");
        html.Append($"<p {p}>{E(t.Hello)}</p>");
        html.Append($"<p {p}>{string.Format(E(t.Intro), $"<b>{E(brand)}</b>")}</p>");
        if (reminderLine is not null)
            html.Append($"<p style=\"margin:0 0 16px;background:#FFF8E1;border-left:3px solid #F9A825;padding:10px 14px;\">{E(reminderLine)}</p>");
        html.Append($"<p {p}>{E(t.What)}</p>");
        html.Append($"<p {p}><b>{E(t.FreeBold)}</b> {E(t.FreeRest)}</p>");
        if (sentLine is not null)
            html.Append($"<p style=\"margin:0 0 16px;background:#F1F8F7;border-left:3px solid {Teal};padding:10px 14px;\">{E(sentLine)}</p>");
        html.Append($"<p style=\"margin:0 0 8px;\"><b>{E(t.HowHeading)}</b></p><ol style=\"margin:0 0 18px;padding-left:20px;\">{how}</ol>");
        html.Append($"<p style=\"margin:0 0 12px;\">{E(t.Ask)}</p>");
        html.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 8px;\">");
        html.Append(Btn(t.BtnYes, Link(Yes), true));
        html.Append(Btn(t.BtnBusiness, Link(Business), false));
        html.Append(Btn(t.BtnNo, Link(No), false));
        html.Append("</table>");
        html.Append($"<p style=\"margin:0 0 22px;color:#78909C;font-size:13px;\">{E(t.OrReply)}</p>");
        html.Append($"<p style=\"margin:0 0 4px;\">{E(t.Bye)}</p>");
        html.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:8px;\"><tr>");
        html.Append($"<td style=\"border-left:3px solid {Teal};padding:2px 0 2px 12px;font-size:14px;line-height:1.5;color:#455A64;\">");
        html.Append($"<span style=\"font-size:16px;font-weight:700;color:{Teal};\">{E(t.Team)}</span><br>{E(t.Line)}<br>");
        html.Append($"<a href=\"mailto:info@ruumly.eu\" style=\"color:{Teal};text-decoration:none;\">info@ruumly.eu</a> &nbsp;·&nbsp; <a href=\"https://ruumly.eu\" style=\"color:{Teal};text-decoration:none;\">ruumly.eu</a>");
        html.Append("</td></tr></table></td></tr>");
        html.Append($"<tr><td style=\"background:#f5f5f5;padding:14px 28px;border-radius:0 0 8px 8px;color:#78909C;font-size:12px;line-height:1.5;\">{Footer(t, brand, html: true)}</td></tr>");
        html.Append("</table>");

        return new Message(lang, subject, text.ToString(), html.ToString());
    }

    private static string Footer(Texts t, string brand, bool html)
    {
        var line2 = string.Format(t.FooterWhy, brand);
        return html
            ? $"{WebUtility.HtmlEncode(t.FooterCompany)}<br>{WebUtility.HtmlEncode(line2)}"
            : $"{t.FooterCompany}\n{line2}";
    }

    // ─── Copy (approved by the founder 2026-11-13, buttons added) ───────────

    public sealed class Texts
    {
        public required string Tag, Subject, ReminderPrefix, Hello, Intro, What, FreeBold, FreeRest, Sent, Reminder,
            HowHeading, Ask, BtnYes, BtnBusiness, BtnNo, OrReply, Bye, Team, Line, FooterCompany, FooterWhy;
        public required string[] How;
        public required Func<int, string> Count;
        // Confirm page
        public required string PageTitle, PageConfirm, PageSubmit, PageOther, PageDone, PageDoneYes, PageDoneBusiness,
            PageDoneNo, PageNotFound;
        public string Free => $"{FreeBold} {FreeRest}";
        public string Label(string answer) => answer switch { Yes => BtnYes, Business => BtnBusiness, _ => BtnNo };

        public static Texts For(string lang) => lang switch
        {
            "et" => Et, "lv" => Lv, "lt" => Lt, _ => En,
        };

        private static readonly Texts Et = new()
        {
            Tag = "partneritele",
            Subject = "Tasuta kliendipäringud ettevõttele {0}",
            ReminderPrefix = "Meeldetuletus: ",
            Hello = "Tere!",
            Intro = "Kirjutame Ruumlyst seoses ettevõttega {0}.",
            What = "Ruumly (ruumly.eu) aitab inimestel, kes kolivad või vajavad lisaruumi, leida usaldusväärse teenusepakkuja: laoruumi, kolimisabi, haagise- ja kaubikurendi või koristuse. Klient kirjeldab oma vajaduse ja me edastame päringu otse sobivatele ettevõtetele tema piirkonnas.",
            FreeBold = "Päringud on Teile tasuta.",
            FreeRest = "Te ei pea looma kontot ega sõlmima lepingut: vastate ainult päringutele, mis Teile sobivad.",
            Sent = "Oleme Teile juba edastanud {0}, kuid vastust me ei saanud. Võimalik, et kirjad jäid rämpsposti kausta.",
            Reminder = "Saatsime Teile selle kirja {0}, kuid vastust me ei saanud. Kui me {2} päeva jooksul vastust ei saa, eemaldame {1} Ruumly teenusepakkujate nimekirjast. Hiljem saate alati tagasi tulla.",
            Count = n => n == 1 ? "ühe kliendipäringu" : $"{n} kliendipäringut",
            HowHeading = "Kuidas see toimib:",
            How = ["Saate e-kirja kliendi vajadusega: teenus, aeg ja asukoht.",
                   "Lingi kaudu saadate oma hinna või loobute päringust.",
                   "Kui klient valib Teie pakkumise, anname Teile kohe teada."],
            Ask = "Palun valige üks vastus, piisab ühest klikist:",
            BtnYes = "Jah, soovin päringuid", BtnBusiness = "Teenindame ainult ettevõtteid", BtnNo = "Ei, ärge rohkem kirjutage",
            OrReply = "Küsimuste korral vastake lihtsalt sellele kirjale.",
            Bye = "Lugupidamisega", Team = "Ruumly meeskond",
            Line = "Hoiustamine, kolimine, koristus, haagise- ja kaubikurent",
            FooterCompany = "Ruumly · Diip Solutions OÜ · registrikood 17527757 · Uus-Sadama tn 15-2, 10120 Tallinn",
            FooterWhy = "Saite selle kirja, sest {0} on Ruumly teenusepakkujate kataloogis. Kui Te ei soovi rohkem kirju, valige „Ei\".",
            PageTitle = "Ruumly partneritele", PageConfirm = "Kinnitage vastus ettevõtte {0} nimel:",
            PageSubmit = "Kinnita", PageOther = "Või valige teine vastus:", PageDone = "Aitäh! Vastus on salvestatud.",
            PageDoneYes = "Saadame Teile edaspidi sobivaid kliendipäringuid. Päringud on tasuta.",
            PageDoneBusiness = "Märkisime, et teenindate ainult ettevõtteid. Eraklientide päringuid me Teile ei saada.",
            PageDoneNo = "Me ei kirjuta Teile enam ja eemaldame ettevõtte kataloogist.",
            PageNotFound = "Link ei kehti. Kirjutage meile: info@ruumly.eu",
        };

        private static readonly Texts Lv = new()
        {
            Tag = "partneriem",
            Subject = "Bezmaksas klientu pieprasījumi uzņēmumam {0}",
            ReminderPrefix = "Atgādinājums: ",
            Hello = "Labdien!",
            Intro = "Rakstām no Ruumly saistībā ar uzņēmumu {0}.",
            What = "Ruumly (ruumly.eu) palīdz cilvēkiem, kuri pārceļas vai kuriem vajag papildu vietu, atrast uzticamu pakalpojumu sniedzēju: noliktavu, palīdzību pārvākšanās laikā, piekabju un busiņu nomu vai uzkopšanu. Klients apraksta savu vajadzību, un mēs pārsūtām pieprasījumu tieši piemērotiem uzņēmumiem viņa reģionā.",
            FreeBold = "Pieprasījumi Jums ir bez maksas.",
            FreeRest = "Nav jāveido konts vai jāslēdz līgums: Jūs atbildat tikai uz tiem pieprasījumiem, kas Jums der.",
            Sent = "Esam Jums jau pārsūtījuši {0}, taču atbildi nesaņēmām. Iespējams, vēstules nonāca mēstuļu mapē.",
            Reminder = "Mēs Jums nosūtījām šo vēstuli {0}, taču atbildi nesaņēmām. Ja {2} dienu laikā atbildi nesaņemsim, mēs izņemsim {1} no Ruumly pakalpojumu sniedzēju saraksta. Vēlāk Jūs vienmēr varat atgriezties.",
            Count = n => n == 1 ? "vienu klienta pieprasījumu"
                : n % 10 == 1 && n % 100 != 11 ? $"{n} klientu pieprasījumu" : $"{n} klientu pieprasījumus",
            HowHeading = "Kā tas notiek:",
            How = ["Jūs saņemat e-pastu ar klienta vajadzību: pakalpojums, laiks un vieta.",
                   "Caur saiti norādāt savu cenu vai atsakāties no pieprasījuma.",
                   "Ja klients izvēlas Jūsu piedāvājumu, mēs Jums uzreiz paziņojam."],
            Ask = "Lūdzu, izvēlieties vienu atbildi, pietiek ar vienu klikšķi:",
            BtnYes = "Jā, vēlos saņemt pieprasījumus", BtnBusiness = "Strādājam tikai ar uzņēmumiem", BtnNo = "Nē, lūdzu, vairs nerakstiet",
            OrReply = "Ja Jums ir jautājumi, vienkārši atbildiet uz šo vēstuli.",
            Bye = "Ar cieņu", Team = "Ruumly komanda",
            Line = "Noliktavas, pārvākšanās, uzkopšana, piekabju un busiņu noma",
            FooterCompany = "Ruumly · Diip Solutions OÜ · reģistrācijas Nr. 17527757 · Uus-Sadama tn 15-2, 10120 Tallina, Igaunija",
            FooterWhy = "Jūs saņēmāt šo vēstuli, jo {0} ir iekļauts Ruumly pakalpojumu sniedzēju katalogā. Ja nevēlaties saņemt vēstules, izvēlieties „Nē\".",
            PageTitle = "Ruumly partneriem", PageConfirm = "Apstipriniet atbildi uzņēmuma {0} vārdā:",
            PageSubmit = "Apstiprināt", PageOther = "Vai izvēlieties citu atbildi:", PageDone = "Paldies! Atbilde ir saglabāta.",
            PageDoneYes = "Turpmāk sūtīsim Jums piemērotus klientu pieprasījumus. Pieprasījumi ir bez maksas.",
            PageDoneBusiness = "Atzīmējām, ka strādājat tikai ar uzņēmumiem. Privātpersonu pieprasījumus Jums nesūtīsim.",
            PageDoneNo = "Mēs Jums vairs nerakstīsim un izņemsim uzņēmumu no kataloga.",
            PageNotFound = "Saite nav derīga. Rakstiet mums: info@ruumly.eu",
        };

        private static readonly Texts Lt = new()
        {
            Tag = "partneriams",
            Subject = "Nemokamos klientų užklausos įmonei {0}",
            ReminderPrefix = "Priminimas: ",
            Hello = "Laba diena!",
            Intro = "Rašome iš Ruumly dėl įmonės {0}.",
            What = "Ruumly (ruumly.eu) padeda žmonėms, kurie kraustosi arba kuriems reikia papildomos vietos, rasti patikimą paslaugų teikėją: sandėliavimo, perkraustymo, priekabų ir mikroautobusų nuomos ar valymo paslaugas. Klientas aprašo savo poreikį, o mes užklausą perduodame tiesiogiai tinkamoms įmonėms jo regione.",
            FreeBold = "Užklausos Jums nemokamos.",
            FreeRest = "Nereikia kurti paskyros ar sudaryti sutarties: atsakote tik į Jums tinkamas užklausas.",
            Sent = "Jau perdavėme Jums {0}, tačiau atsakymo negavome. Galbūt laiškai pateko į šlamšto aplanką.",
            Reminder = "Šį laišką Jums išsiuntėme {0}, tačiau atsakymo negavome. Jei per {2} dienų atsakymo negausime, {1} pašalinsime iš Ruumly paslaugų teikėjų sąrašo. Vėliau visada galėsite sugrįžti.",
            Count = n => n == 1 ? "vieną kliento užklausą"
                : n % 10 >= 2 && n % 10 <= 9 && !(n % 100 >= 11 && n % 100 <= 19) ? $"{n} klientų užklausas" : $"{n} klientų užklausų",
            HowHeading = "Kaip tai veikia:",
            How = ["Gaunate el. laišką su kliento poreikiu: paslauga, laikas ir vieta.",
                   "Per nuorodą nurodote savo kainą arba atsisakote užklausos.",
                   "Jei klientas pasirenka Jūsų pasiūlymą, iškart Jums pranešame."],
            Ask = "Prašome pasirinkti vieną atsakymą, pakanka vieno paspaudimo:",
            BtnYes = "Taip, noriu gauti užklausas", BtnBusiness = "Dirbame tik su įmonėmis", BtnNo = "Ne, prašome daugiau nerašyti",
            OrReply = "Jei turite klausimų, tiesiog atsakykite į šį laišką.",
            Bye = "Pagarbiai", Team = "Ruumly komanda",
            Line = "Sandėliavimas, perkraustymas, valymas, priekabų ir mikroautobusų nuoma",
            FooterCompany = "Ruumly · Diip Solutions OÜ · įmonės kodas 17527757 · Uus-Sadama tn 15-2, 10120 Talinas, Estija",
            FooterWhy = "Gavote šį laišką, nes {0} yra įtraukta į Ruumly paslaugų teikėjų katalogą. Jei nenorite gauti laiškų, pasirinkite „Ne\".",
            PageTitle = "Ruumly partneriams", PageConfirm = "Patvirtinkite atsakymą įmonės {0} vardu:",
            PageSubmit = "Patvirtinti", PageOther = "Arba pasirinkite kitą atsakymą:", PageDone = "Ačiū! Atsakymas išsaugotas.",
            PageDoneYes = "Toliau siųsime Jums tinkamas klientų užklausas. Užklausos nemokamos.",
            PageDoneBusiness = "Pažymėjome, kad dirbate tik su įmonėmis. Privačių asmenų užklausų Jums nesiųsime.",
            PageDoneNo = "Daugiau Jums nerašysime ir pašalinsime įmonę iš katalogo.",
            PageNotFound = "Nuoroda negalioja. Rašykite mums: info@ruumly.eu",
        };

        private static readonly Texts En = new()
        {
            Tag = "for partners",
            Subject = "Free customer requests for {0}",
            ReminderPrefix = "Reminder: ",
            Hello = "Hello,",
            Intro = "We are writing from Ruumly about {0}.",
            What = "Ruumly (ruumly.eu) helps people who are moving or need extra space find a reliable provider: storage, help with moving, trailer and van rental, or cleaning. The customer describes what they need and we forward the request directly to suitable companies in their area.",
            FreeBold = "The requests are free for you.",
            FreeRest = "No account or contract is needed: you answer only the requests that suit you.",
            Sent = "We have already forwarded {0} to you but did not hear back. The messages may have landed in your spam folder.",
            Reminder = "We sent you this message on {0} but did not hear back. If we do not hear from you within {2} days, we will remove {1} from Ruumly's list of providers. You can always come back later.",
            Count = n => n == 1 ? "one customer request" : $"{n} customer requests",
            HowHeading = "How it works:",
            How = ["You receive an e-mail with the customer's need: service, date and location.",
                   "Through a link you send your price or decline the request.",
                   "If the customer chooses your offer, we let you know straight away."],
            Ask = "Please choose one answer. One click is enough:",
            BtnYes = "Yes, send me requests", BtnBusiness = "We serve businesses only", BtnNo = "No, please stop writing",
            OrReply = "If you have questions, simply reply to this e-mail.",
            Bye = "Kind regards", Team = "The Ruumly team",
            Line = "Storage, moving, cleaning, trailer and van rental",
            FooterCompany = "Ruumly · Diip Solutions OÜ · registry code 17527757 · Uus-Sadama tn 15-2, 10120 Tallinn, Estonia",
            FooterWhy = "You received this because {0} is listed in Ruumly's provider directory. If you do not want further e-mails, choose \"No\".",
            PageTitle = "Ruumly for partners", PageConfirm = "Confirm the answer on behalf of {0}:",
            PageSubmit = "Confirm", PageOther = "Or choose another answer:", PageDone = "Thank you! Your answer is saved.",
            PageDoneYes = "We will send you suitable customer requests. The requests are free.",
            PageDoneBusiness = "We noted that you serve businesses only. We will not send you requests from private customers.",
            PageDoneNo = "We will not write to you again and will remove the company from the directory.",
            PageNotFound = "This link is not valid. Write to us: info@ruumly.eu",
        };
    }
}
