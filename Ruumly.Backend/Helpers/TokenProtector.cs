using Microsoft.AspNetCore.DataProtection;

namespace Ruumly.Backend.Helpers;

public class TokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector =
        provider.CreateProtector("Ruumly.SupplierApiTokens");

    public string Protect(string plaintext) =>
        _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return null;
        try { return _protector.Unprotect(ciphertext); }
        catch { return null; }
    }
}
