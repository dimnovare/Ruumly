using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/provider")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderBankController(RuumlyDbContext db) : ControllerBase
{
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
            if (cleanIban.Length < 15 || cleanIban.Length > 34)
                return BadRequest(new { error = "Invalid IBAN format." });
            supplier.Iban = cleanIban;
        }

        if (body.BankAccountName is not null)
            supplier.BankAccountName = body.BankAccountName.Trim();
        if (body.BankName is not null)
            supplier.BankName = body.BankName.Trim();

        supplier.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new
        {
            iban            = supplier.Iban,
            bankAccountName = supplier.BankAccountName,
            bankName        = supplier.BankName,
        });
    }
}
