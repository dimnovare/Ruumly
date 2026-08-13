namespace Ruumly.Backend.DTOs.Requests;

/// <summary>
/// Turns a verified claim session into a real provider login.
///
/// No email field on purpose. The address is taken from the claim itself — the
/// one the magic link was actually delivered to — never from the request body
/// and never from <c>Supplier.ContactEmail</c>, which the same session is
/// allowed to rewrite moments earlier. Accepting an address here would let a
/// claimant mint an account for a mailbox they have not proved they control.
/// </summary>
public record ClaimCreateAccountRequest(string Password);
