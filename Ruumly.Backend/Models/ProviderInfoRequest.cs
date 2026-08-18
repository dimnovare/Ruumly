using System.ComponentModel.DataAnnotations;

namespace Ruumly.Backend.Models;

/// <summary>
/// A provider saying, on the record, "I cannot quote this yet — here is what is
/// missing."
///
/// Until this existed the quote page offered exactly ONE action: submit a price.
/// A provider who could not do that had nowhere to say so, so the answer arrived
/// as free-text mail into a shared ops inbox with nothing on it naming the lead.
/// That is not a hypothetical: on 2026-08-17 Adduco replied to a live Haapsalu
/// move with "selle info pealt adekvaatset pakkumist paraku ei saa teha" and
/// asked whether the two ends were the same address, plus photos. It cost a full
/// round trip on a job the customer needed that same week — and it was invisible
/// to every metric we have, because to the funnel that outreach was still
/// silence.
///
/// So this row exists to make the block a FIRST-CLASS FACT rather than a mail:
/// it names the lead, the supplier and the exact token that was used, it is
/// countable, and <see cref="ResolvedAt"/> lets ops close it.
///
/// The customer is deliberately NOT emailed when one of these is written.
/// Deciding what to ask a person, and in which language, is a human call for now.
/// </summary>
public class ProviderInfoRequest
{
    public Guid Id { get; set; }

    public Guid DemandLeadId { get; set; }
    public DemandLead? DemandLead { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>
    /// WHICH outreach — i.e. which quote token — was used, not merely which
    /// supplier. One company can be contacted about the same lead twice (a
    /// resend, or two branch rows), and the answer to "who is blocked, on what
    /// link" is what ops needs to reply to the right thread.
    /// </summary>
    public Guid ProviderOutreachId { get; set; }
    public ProviderOutreach? ProviderOutreach { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The structured set of what is missing: a JSON array of
    /// <see cref="Constants.InfoRequestReasons"/> slugs, e.g. ["photos","address"].
    ///
    /// Same shape and same reasoning as <see cref="DemandLead.PhotoKeysJson"/>
    /// and Supplier.ServiceTypesJson — a JSON array in a text column, tolerant on
    /// read. A seventh reason is then one string in a constant, with no migration
    /// and no historical row to rewrite; and unlike a column that persists enum
    /// NAMES, retiring one later cannot make old rows unreadable.
    ///
    /// May legitimately be an empty array: a provider who ticks nothing but
    /// writes out the question in <see cref="Note"/> has still told us everything
    /// we needed. What is refused is neither.
    /// </summary>
    public string? ReasonsJson { get; set; }

    /// <summary>
    /// The provider's own words. Clamped at 2000 like every other provider-authored
    /// note in this schema (ProviderOutreach.Note/QuotedNote, DemandLead.ProviderNotes).
    /// In practice this is the field that actually closes the loop — the checkboxes
    /// say which question, this says what the question is.
    /// </summary>
    [MaxLength(2000)] public string? Note { get; set; }

    /// <summary>
    /// When ops answered it. Null means still open — which is what the quote page
    /// reads back to tell the provider we have their ask, and what a future
    /// "blocked requests" queue would count.
    /// </summary>
    public DateTime? ResolvedAt { get; set; }
}
