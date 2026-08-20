using System.ComponentModel.DataAnnotations;

namespace Ruumly.Backend.Models;

/// <summary>
/// One message an operator sent by hand about a <see cref="DemandLead"/> — to the
/// customer, or to a provider already contacted for it.
///
/// WHY THIS EXISTS. The concierge loop is a conversation, and until now the
/// product could send only the messages it composed itself: the outreach letter,
/// the offer, the ops alerts. Anything else — "what is the exact address?",
/// "can you confirm that price is per hour?", "nobody in your area could take
/// this" — had to be sent from a personal mailbox, because there was no endpoint
/// for it. On 2026-08-20 exactly that happened: four messages about a live
/// Latvian request, including one to the customer, left from the founder's
/// personal Gmail signed as Ruumly, and both provider replies landed somewhere
/// the ops loop cannot see.
///
/// So the point of this row is not the email. It is that the email STOPS BEING
/// INVISIBLE: it hangs off the lead, the workspace can show it, and the next
/// person to open that lead can see what was already asked and by whom.
///
/// DELIBERATELY NOT A FULL INBOX. Nothing here parses replies — those still
/// arrive in the ops mailbox, and inbound processing is Phase 2. This records
/// only what WE sent, which is the half that was missing entirely.
/// </summary>
public class LeadMessage
{
    public Guid Id { get; set; }

    public Guid DemandLeadId { get; set; }
    public DemandLead? DemandLead { get; set; }

    /// <summary>
    /// The provider this went to, when it went to a provider. Null for a message
    /// to the customer. Plain column, no FK cascade — the message must outlive a
    /// supplier row being purged, the same way OfferOption.CreatedFromOutreachId
    /// outlives its outreach.
    /// </summary>
    public Guid? SupplierId { get; set; }

    /// <summary>
    /// Address the message was actually sent to, snapshotted. Contact emails get
    /// corrected (a bounced provider address was fixed twice this month alone),
    /// and the history has to say where the mail really went, not where it would
    /// go today.
    /// </summary>
    [MaxLength(200)] public string SentTo { get; set; } = string.Empty;

    [MaxLength(300)]  public string Subject { get; set; } = string.Empty;
    [MaxLength(10000)] public string Body { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>The admin who sent it — this is correspondence, so it has an author.</summary>
    public Guid? SentByUserId { get; set; }
}
