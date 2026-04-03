using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/provider")]
[Authorize(Roles = "Provider,Admin")]
public class ProviderBankController(RuumlyDbContext db) : ControllerBase
{
    [HttpGet("bank-details")]
    [HttpGet("/api/admin/my-bank-details")]  // backward compat
    public async Task<IActionResult> GetBankDetails()
    {
        var userId   = User.GetUserId();
        var user     = await db.Users
            .Include(u => u.Supplier)
            .FirstOrDefaultAsync(u => u.Id == userId);
        var supplier = user?.Supplier;
        if (supplier is null)
            return BadRequest(new { error = ErrorMessages.Get("NO_SUPPLIER_LINKED", Request.GetLang()) });

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
        var userId = User.GetUserId();
        var user   = await db.Users
            .Include(u => u.Supplier)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.SupplierId is null)
            return BadRequest(new { error = ErrorMessages.Get("NO_SUPPLIER_LINKED", Request.GetLang()) });

        var supplier = await db.Suppliers.FindAsync(user.SupplierId.Value);
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

    [HttpGet("/api/supplier/stats")]
    public async Task<IActionResult> GetSupplierStats([FromQuery] Guid? supplierId = null)
    {
        Guid effectiveSupplierId;
        if (supplierId.HasValue && User.GetUserRole() == UserRole.Admin)
        {
            effectiveSupplierId = supplierId.Value;
        }
        else
        {
            var user = await db.Users.FindAsync(User.GetUserId());
            if (user?.SupplierId is null) return BadRequest(new { error = "No supplier linked." });
            effectiveSupplierId = user.SupplierId.Value;
        }

        var now        = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var allBookings = await db.Bookings
            .Where(b => b.SupplierId == effectiveSupplierId && !b.IsDeleted)
            .ToListAsync();

        var thisMonth      = allBookings.Where(b => b.CreatedAt >= monthStart).ToList();
        var activeBookings = allBookings.Where(b =>
            b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active).ToList();

        var totalUnits  = await db.Listings.CountAsync(l => l.SupplierId == effectiveSupplierId && l.IsActive);
        var bookedUnits = activeBookings.Select(b => b.ListingId).Distinct().Count();

        return Ok(new
        {
            totalBookings     = allBookings.Count,
            thisMonthBookings = thisMonth.Count,
            thisMonthRevenue  = thisMonth.Sum(b => b.Total),
            activeBookings    = activeBookings.Count,
            totalUnits,
            bookedUnits,
            occupancyRate = totalUnits > 0
                ? Math.Round((decimal)bookedUnits / totalUnits * 100, 1)
                : 0,
            totalRevenue = allBookings
                .Where(b => b.Status != BookingStatus.Cancelled)
                .Sum(b => b.Total),
        });
    }
}
