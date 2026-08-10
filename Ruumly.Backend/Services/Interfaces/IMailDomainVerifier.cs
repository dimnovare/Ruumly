namespace Ruumly.Backend.Services.Interfaces;

/// <summary>
/// Answers one question about a batch of email domains: which of them provably
/// cannot receive mail?
///
/// This exists because the directory was built from public research, and a
/// scraped address is only as good as the domain behind it. The 2026-08-09 MX
/// audit found five stored addresses on domains that are gone or run no mail
/// server — they had been silently swallowing customer requests, indistinguishable
/// from a provider who simply never replied.
///
/// The contract is deliberately negative — it returns only what is PROVEN
/// undeliverable, never "these are good". A lookup that fails, times out or comes
/// back ambiguous must leave the domain OUT of the result: an unreachable resolver
/// is not evidence against a provider, and failing the other way would silently
/// shrink a campaign whenever DNS hiccups.
/// </summary>
public interface IMailDomainVerifier
{
    /// <summary>
    /// Returns the subset of <paramref name="domains"/> that has no usable MX —
    /// the domain does not exist, or it resolves but publishes no mail exchanger
    /// (including RFC 7505 "null MX", which is an explicit refusal to accept mail).
    ///
    /// Comparison is case-insensitive and the returned set is lower-cased.
    /// Domains whose status could not be established are NOT in the result.
    /// </summary>
    Task<IReadOnlySet<string>> FindUndeliverableAsync(
        IEnumerable<string> domains, CancellationToken ct = default);
}
