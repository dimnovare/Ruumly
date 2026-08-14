namespace Ruumly.Backend.Helpers;

/// <summary>
/// Pragmatic, dependency-free email validation. Requires non-empty, length &lt;= 200,
/// and roughly the shape local@domain.tld — exactly one '@' (not at index 0), a '.'
/// after the '@', no leading/trailing '.' in the domain, a final TLD segment of at
/// least 2 chars, and no whitespace anywhere. Not RFC-perfect, but rejects the common
/// garbage ('a@b', 'user@', '@x.com') that a bare Contains('@') check let through.
/// </summary>
public static class EmailValidation
{
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var value = email.Trim();

        if (value.Length > 200)
            return false;

        // Reject any internal whitespace (the trim only handles the ends).
        foreach (var c in value)
            if (char.IsWhiteSpace(c))
                return false;

        // Exactly one '@', and not the first character (no empty local part).
        var at = value.IndexOf('@');
        if (at <= 0)
            return false;
        if (value.IndexOf('@', at + 1) != -1)
            return false;

        var domain = value[(at + 1)..];

        // Domain must be non-empty, contain a dot, and not start/end with a dot.
        if (domain.Length == 0)
            return false;
        if (domain[0] == '.' || domain[^1] == '.')
            return false;

        var lastDot = domain.LastIndexOf('.');
        if (lastDot < 0)
            return false;

        // Final TLD segment must be at least 2 chars.
        var tld = domain[(lastDot + 1)..];
        if (tld.Length < 2)
            return false;

        return true;
    }

    /// <summary>
    /// The canonical stored form of an address: trimmed and lower-cased. Mail is
    /// delivered case-insensitively, so "Info@Yard.ee" and "info@yard.ee" are one
    /// mailbox and must never become two rows. Anything persisting an address as an
    /// IDENTITY — <c>User.Email</c> above all, since its unique index is what decides
    /// who owns an account — stores this form and compares against this form, so
    /// "the same address" has exactly one definition.
    /// </summary>
    public static string Normalize(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// The domain part of an address, lower-cased, or null if the address is not
    /// <see cref="IsValid">valid</see>. Callers that group addresses by domain (the
    /// deliverability check does) must use this rather than splitting on '@'
    /// themselves, so "what counts as a domain" has exactly one definition.
    /// </summary>
    public static string? DomainOf(string? email)
    {
        if (!IsValid(email))
            return null;

        var value = email!.Trim();
        return value[(value.IndexOf('@') + 1)..].ToLowerInvariant();
    }
}
