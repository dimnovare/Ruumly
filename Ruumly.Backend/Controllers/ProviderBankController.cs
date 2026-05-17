using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/provider")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderBankController(
    RuumlyDbContext db,
    ILogger<ProviderBankController> logger) : ControllerBase
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates an IBAN using the ISO 13616 mod-97 checksum.
    /// Expects a pre-cleaned, uppercased string (no spaces).
    /// </summary>
    private static bool IsValidIban(string iban)
    {
        if (iban.Length < 15 || iban.Length > 34) return false;
        if (!System.Text.RegularExpressions.Regex.IsMatch(iban, @"^[A-Z]{2}\d{2}[A-Z0-9]+$"))
            return false;

        // Move first 4 chars to end, convert letters to digits, check mod 97 == 1
        var rearranged = iban[4..] + iban[..4];
        var numeric = string.Concat(rearranged.Select(c =>
            char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));

        var remainder = numeric.Aggregate(0, (acc, c) =>
            (int)((acc * 10 + (c - '0')) % 97));

        return remainder == 1;
    }

    // Admin: ?supplierId= query param. Provider: own user.SupplierId.
    private async Task<Guid?> ResolveSupplierIdAsync()
    {
        if (User.IsInRole("Admin"))
        {
            if (Request.Query.TryGetValue("supplierId", out var sv) &&
                Guid.TryParse(sv, out var sid))
                return sid;
            return null;
        }
        var user = await db.Users.FindAsync(User.GetUserId());
        return user?.SupplierId;
    }

    [HttpGet("bank-details")]
    [HttpGet("/api/admin/my-bank-details")]  // backward compat
    public async Task<IActionResult> GetBankDetails()
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to view this resource.",
                    hint    = "Pick a supplier from /admin/suppliers and pass its id as a query param.",
                });
            return BadRequest(new { error = ErrorMessages.Get("NO_SUPPLIER_LINKED", Request.GetLang()) });
        }

        var supplier = await db.Suppliers.FindAsync(supplierId.Value);
        if (supplier is null) return NotFound(new { error = "Supplier not found." });

        return Ok(new
        {
            iban            = supplier.Iban,
            bankAccountName = supplier.BankAccountName,
            bankName        = supplier.BankName,
        });
    }

    [HttpPatch("bank-details")]
    [HttpPatch("/api/admin/my-bank-details")]  // backward compat
    [EnableRateLimiting("user")]
    public async Task<IActionResult> UpdateBankDetails([FromBody] UpdateBankDetailsRequest body)
    {
        var supplierId = await ResolveSupplierIdAsync();
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to view this resource.",
                    hint    = "Pick a supplier from /admin/suppliers and pass its id as a query param.",
                });
            return BadRequest(new { error = ErrorMessages.Get("NO_SUPPLIER_LINKED", Request.GetLang()) });
        }

        var supplier = await db.Suppliers.FindAsync(supplierId.Value);
        if (supplier is null) return NotFound(new { error = "Supplier not found." });

        if (!string.IsNullOrWhiteSpace(body.Iban))
        {
            var cleanIban = body.Iban.Replace(" ", "").ToUpper();
            if (!IsValidIban(cleanIban))
                return BadRequest(new { error = "Invalid IBAN — please check the account number." });
            supplier.Iban = cleanIban;
        }

        if (body.BankAccountName is not null)
            supplier.BankAccountName = body.BankAccountName.Trim();
        if (body.BankName is not null)
            supplier.BankName = body.BankName.Trim();

        supplier.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(body.Iban))
        {
            logger.LogInformation(
                "IBAN updated for supplier {SupplierId} by user {UserId}",
                supplierId, User.GetUserId());

            db.AuditLogs.Add(new AuditLog
            {
                Id        = Guid.NewGuid(),
                Action    = "bank_details.iban_updated",
                Actor     = User.GetUserEmail(),
                Target    = supplierId.Value.ToString(),
                Detail    = $"IBAN ending ...{supplier.Iban?[^4..]}",
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        return Ok(new
        {
            iban            = supplier.Iban,
            bankAccountName = supplier.BankAccountName,
            bankName        = supplier.BankName,
        });
    }
}
