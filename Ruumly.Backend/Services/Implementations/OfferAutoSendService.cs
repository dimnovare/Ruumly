using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

/// <summary>
/// Releases a provider-seeded draft offer to the customer without an admin
/// touching it.
///
/// Why this exists: every quote currently passes through the founder — read the
/// mail, open the workspace, fix the unit, press send. That is why the median
/// first response was measured in hours rather than minutes, and it is the one
/// thing that makes request volume and the founder's day grow together.
///
/// DEFAULT OFF (<c>offerAutoSend</c>). At the volume this shipped into — a
/// handful of requests a month — a human reading each offer is still better
/// than a rule, and the cost of a bad automatic send to a real customer is far
/// higher than the minutes it saves. This is built so it is ready and tested
/// when volume makes it worth flipping, not because it is worth flipping today.
///
/// Two triggers, whichever comes first:
/// <list type="bullet">
/// <item><c>offerAutoSendMinQuotes</c> quotes have arrived — the customer was
/// promised "2–3 offers", so waiting for a second is what makes the promise
/// true rather than sending the first price that lands.</item>
/// <item><c>offerAutoSendWaitMinutes</c> have passed since the FIRST quote —
/// because most requests will never collect a second, and a good single offer
/// an hour later beats a perfect pair that never comes.</item>
/// </list>
/// </summary>
public class OfferAutoSendService(
    RuumlyDbContext db,
    IBackgroundEmailQueue emailQueue,
    IConfiguration config,
    ILogger<OfferAutoSendService> logger) : IOfferAutoSendService
{
    public const string EnabledSetting      = "offerAutoSend";
    public const string MinQuotesSetting    = "offerAutoSendMinQuotes";
    public const string WaitMinutesSetting  = "offerAutoSendWaitMinutes";

    private const bool DefaultEnabled     = false;
    private const int  DefaultMinQuotes   = 2;
    private const int  DefaultWaitMinutes = 90;

    public async Task<OfferAutoSendResult> TrySendAsync(Guid offerId, CancellationToken ct = default)
    {
        if (!await EnabledAsync(ct)) return OfferAutoSendResult.Skipped("disabled");

        var offer = await db.Offers
            .Include(o => o.Options)
            .Include(o => o.DemandLead)
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);

        if (offer is null)                       return OfferAutoSendResult.Skipped("not_found");
        // An admin who already sent, or a customer who already chose, outranks
        // any rule here. Only an untouched draft is ours to release.
        if (offer.Status != OfferStatus.Draft)   return OfferAutoSendResult.Skipped("not_draft");
        if (offer.Options.Count == 0)            return OfferAutoSendResult.Skipped("no_options");

        var lead = offer.DemandLead;
        if (lead is null)                        return OfferAutoSendResult.Skipped("no_lead");
        if (string.IsNullOrWhiteSpace(lead.Email)) return OfferAutoSendResult.Skipped("no_email");
        // A closed request takes no offer: the customer has already been served,
        // dismissed or given up.
        if (lead.Status is DemandLeadStatus.Converted or DemandLeadStatus.Dismissed
                        or DemandLeadStatus.Unmatched)
            return OfferAutoSendResult.Skipped("lead_closed");

        var quoted = offer.Options.Count(o => o.CreatedFromOutreachId is not null);
        var minQuotes = await IntSettingAsync(MinQuotesSetting, DefaultMinQuotes, ct);
        var waitFrom  = offer.Options
            .Where(o => o.CreatedFromOutreachId is not null)
            .Select(o => (DateTime?)offer.CreatedAt)
            .FirstOrDefault() ?? offer.CreatedAt;
        var waitMinutes = await IntSettingAsync(WaitMinutesSetting, DefaultWaitMinutes, ct);
        var waited      = DateTime.UtcNow - waitFrom >= TimeSpan.FromMinutes(waitMinutes);

        if (quoted < minQuotes && !waited)
            return OfferAutoSendResult.Skipped("waiting_for_more");

        offer.Status = OfferStatus.Sent;
        offer.SentAt = DateTime.UtcNow;
        offer.Version++;

        if (lead.Status != DemandLeadStatus.Converted)
            DemandLeadLifecycle.MoveTo(lead, DemandLeadStatus.Quoted);

        db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = "offer.sent_auto",
            Actor     = "system",
            Target    = offer.Id.ToString(),
            Detail    = $"Auto-sent to the customer with {offer.Options.Count} option(s), " +
                        $"{quoted} from provider quotes. Trigger: " +
                        $"{(quoted >= minQuotes ? $"reached {minQuotes} quotes" : $"waited {waitMinutes} min")}.",
            CreatedAt = DateTime.UtcNow,
        });

        // Save BEFORE queueing: Hangfire commits on its own connection, so a
        // failed save must never leave the customer holding a live link to an
        // offer this transaction rolled back.
        await db.SaveChangesAsync(ct);

        var link  = FrontendUrl.Localized(config["AppUrl"], offer.Language, $"offer/{offer.Token}");
        var email = OfferDeliveryComposer.ComposeEmail(offer, link);
        emailQueue.EnqueueEmail(
            lead.Email.Trim(), email.Subject, email.TextBody,
            htmlBody: null, replyTo: await OpsInbox.ResolveAsync(db));

        logger.LogInformation(
            "Offer {OfferId} auto-sent for lead {LeadId} ({Options} option(s), {Quoted} quoted).",
            offer.Id, lead.Id, offer.Options.Count, quoted);

        return OfferAutoSendResult.Sent(offer.Options.Count);
    }

    private async Task<bool> EnabledAsync(CancellationToken ct)
    {
        var raw = await db.PlatformSettings
            .Where(s => s.Key == EnabledSetting)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return raw is null ? DefaultEnabled : raw == "true";
    }

    private async Task<int> IntSettingAsync(string key, int fallback, CancellationToken ct)
    {
        var raw = await db.PlatformSettings
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
