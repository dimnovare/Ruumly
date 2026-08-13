namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// Records a business asking to be left alone — the REMOVE reply the
/// introduction email promises to honour — or, with
/// <paramref name="Reactivate"/>, that business asking to come back.
/// </summary>
/// <param name="Reason">
/// How the request arrived ("REMOVE reply 2026-08-13", "asked on the phone").
/// Free text, never shown publicly. Worth filling in: it is what lets a later
/// reader tell a real request from a mistaken bulk edit.
/// </param>
/// <param name="Reactivate">
/// Clears the opt-out and restores the listing. Only ever set this when the
/// business itself asks — it is the one thing that undoes their decision.
/// Defaults to false so the dangerous direction is never the default.
/// </param>
public record SupplierOptOutRequest(
    string? Reason = null,
    bool Reactivate = false);
