using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using System.Text;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/supplier")]
[Authorize(Roles = "Provider,Admin")]
public class SupplierTeamController(
    RuumlyDbContext db,
    IAuthService authService,
    IPricingConfigService pricingConfigService) : ControllerBase
{
    // GET /api/supplier/team
    [HttpGet("team")]
    public async Task<IActionResult> GetTeam()
    {
        var userId     = User.GetUserId();
        var supplierId = await GetSupplierIdAsync(userId);
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to view this resource.",
                    hint    = "Pick a supplier from /admin/suppliers and pass its id as a query param.",
                });
            return NotFound(new { message = "No supplier linked to this account." });
        }

        var owner = await db.Users
            .Where(u => u.SupplierId == supplierId && u.Role == UserRole.Provider)
            .OrderBy(u => u.RegisteredAt)
            .FirstOrDefaultAsync();

        // Capture as a value-typed Guid? so the projection below has nothing
        // to dereference at SQL-translation time — `u.Id == owner!.Id` would
        // NRE inside EF's expression visitor when owner is null.
        var ownerId = owner?.Id;

        var members = await db.Users
            .Where(u => u.SupplierId == supplierId && u.Role == UserRole.Provider)
            .OrderBy(u => u.RegisteredAt)
            .Select(u => new TeamMemberDto(
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Role.ToString(),
                ownerId.HasValue && u.Id == ownerId.Value,
                u.LastLoginAt.HasValue ? u.LastLoginAt.Value.ToString("yyyy-MM-dd HH:mm") : null,
                u.RegisteredAt.ToString("yyyy-MM-dd")))
            .ToListAsync();

        return Ok(members);
    }

    // POST /api/supplier/team/invite
    [HttpPost("team/invite")]
    public async Task<IActionResult> InviteMember([FromBody] InviteTeamMemberRequest request)
    {
        var userId     = User.GetUserId();
        var supplierId = await GetSupplierIdAsync(userId);
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to view this resource.",
                    hint    = "Pick a supplier from /admin/suppliers and pass its id as a query param.",
                });
            return NotFound(new { message = "No supplier linked to this account." });
        }

        // Find or create the invited user
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (existing is not null)
        {
            if (existing.SupplierId == supplierId)
                return Conflict(new { message = "User is already a member of this team." });

            existing.SupplierId = supplierId;
            existing.Role       = UserRole.Provider;
        }
        else
        {
            var newUser = new User
            {
                Id           = Guid.NewGuid(),
                Name         = request.Name,
                Email        = request.Email,
                PasswordHash = string.Empty,
                Role         = UserRole.Provider,
                SupplierId   = supplierId,
                RegisteredAt = DateTime.UtcNow,
            };
            db.Users.Add(newUser);
        }

        await db.SaveChangesAsync();

        // Send password-reset email so the invited user can set their password
        await authService.RequestPasswordResetAsync(request.Email);

        return Ok(new { message = "Invitation sent." });
    }

    // DELETE /api/supplier/team/{userId}
    [HttpDelete("team/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid memberId)
    {
        var callerId   = User.GetUserId();
        var supplierId = await GetSupplierIdAsync(callerId);
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to view this resource.",
                    hint    = "Pick a supplier from /admin/suppliers and pass its id as a query param.",
                });
            return NotFound(new { message = "No supplier linked to this account." });
        }

        if (memberId == callerId)
            return BadRequest(new { message = "You cannot remove yourself from the team." });

        var member = await db.Users
            .FirstOrDefaultAsync(u => u.Id == memberId && u.SupplierId == supplierId);

        if (member is null)
            return NotFound(new { message = "Team member not found." });

        // Determine owner (earliest Provider on this supplier) — owner cannot be removed
        var ownerId = await db.Users
            .Where(u => u.SupplierId == supplierId && u.Role == UserRole.Provider)
            .OrderBy(u => u.RegisteredAt)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        if (memberId == ownerId)
            return BadRequest(new { message = "The supplier owner cannot be removed from the team." });

        member.SupplierId = null;
        member.Role       = UserRole.Customer;
        await db.SaveChangesAsync();

        return NoContent();
    }

    // GET /api/supplier/profile
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var supplierId = await GetSupplierIdAsync(User.GetUserId());
        if (supplierId is null)
        {
            if (User.IsInRole("Admin"))
                return BadRequest(new
                {
                    error   = "supplier_context_required",
                    message = "Admin must specify ?supplierId= to view this resource.",
                    hint    = "Pick a supplier from /admin/suppliers and pass its id as a query param.",
                });
            return NotFound(new { message = "No supplier profile linked to this account." });
        }

        var supplier = await db.Suppliers
            .Include(s => s.IntegrationSettings)
            .FirstOrDefaultAsync(s => s.Id == supplierId.Value);
        if (supplier is null)
            return NotFound(new { message = "Supplier not found." });

        var ordersTotal = await db.Orders
            .Where(o => o.SupplierId == supplier.Id)
            .CountAsync();

        var revenue = await db.PayoutEntries
            .Where(p => p.SupplierId == supplier.Id && p.Status == PayoutStatus.Paid)
            .SumAsync(p => (decimal?)p.SupplierAmount) ?? 0m;

        var pricingConfig = await pricingConfigService.GetAsync();
        var dto           = AdminMappers.MapSupplier(
            supplier,
            ordersTotal:     ordersTotal,
            revenue:         revenue,
            includeSettings: true,
            pricingConfig:   pricingConfig);

        return Ok(dto);
    }

    // PATCH /api/supplier/profile
    [HttpPatch("profile")]
    [Authorize(Roles = "Provider")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateSupplierProfileRequest body)
    {
        var userId = User.GetUserId();
        var supplier = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Supplier)
            .FirstOrDefaultAsync();

        if (supplier is null)
            return NotFound(new { message = "No supplier profile linked to this account." });

        if (body.Name is not null)
        {
            var trimmed = body.Name.Trim();
            if (trimmed.Length < 2 || trimmed.Length > 200)
                return BadRequest(new { error = "Company name must be 2-200 characters." });
            supplier.Name = trimmed;
        }

        if (body.RegistryCode is not null)
        {
            // Allow empty (clearing the code) but cap length
            var trimmed = body.RegistryCode.Trim();
            if (trimmed.Length > 50)
                return BadRequest(new { error = "Registry code too long." });
            supplier.RegistryCode = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        if (body.ContactName  is not null) supplier.ContactName  = body.ContactName;
        if (body.ContactEmail is not null) supplier.ContactEmail = body.ContactEmail;
        if (body.ContactPhone is not null) supplier.ContactPhone = body.ContactPhone;

        supplier.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new
        {
            supplier.Name,
            supplier.RegistryCode,
            supplier.ContactName,
            supplier.ContactEmail,
            supplier.ContactPhone,
        });
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        bool aggregateAll = false;
        Guid? effectiveSupplierId = null;

        if (User.GetUserRole() == UserRole.Admin)
        {
            if (Request.Query.TryGetValue("supplierId", out var sv) &&
                Guid.TryParse(sv, out var sid))
                effectiveSupplierId = sid;
            else
                aggregateAll = true;
        }
        else
        {
            var supplierId = await GetSupplierIdAsync(User.GetUserId());
            if (supplierId is null)
                return BadRequest(new { error = "No supplier linked." });
            effectiveSupplierId = supplierId;
        }

        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);

        // Monthly bookings + revenue for the last 6 months
        var ordersQuery = db.Orders.Where(o => o.CreatedAt >= sixMonthsAgo);
        if (!aggregateAll)
            ordersQuery = ordersQuery.Where(o => o.SupplierId == effectiveSupplierId);

        var monthly = await ordersQuery
            .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
            .Select(g => new
            {
                Year     = g.Key.Year,
                Month    = g.Key.Month,
                Bookings = g.Count(),
                Revenue  = g.Sum(o => o.Total),
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync();

        // Total views across all listings (per-supplier or platform-wide)
        var listingsQuery = db.Listings.AsQueryable();
        if (!aggregateAll)
            listingsQuery = listingsQuery.Where(l => l.SupplierId == effectiveSupplierId);
        var totalViews = await listingsQuery.SumAsync(l => l.ViewCount);

        return Ok(new { monthly, totalViews });
    }

    // GET /api/supplier/calendar.ics
    [HttpGet("/api/supplier/calendar.ics")]
    [Authorize(Roles = "Provider")]
    public async Task<IActionResult> ExportIcal()
    {
        var userId = User.GetUserId();
        var user = await db.Users.FindAsync(userId);
        if (user?.SupplierId is null) return NotFound();

        var supplier = await db.Suppliers.FindAsync(user.SupplierId);
        if (supplier is null) return NotFound();

        var config = await pricingConfigService.GetAsync();
        if (!config.ForTier(supplier.Tier).HasCalendarSync)
            return Forbid("Calendar sync requires Business plan.");

        var bookings = await db.Bookings
            .Include(b => b.Listing)
            .Where(b => b.SupplierId == user.SupplierId &&
                   (b.Status == BookingStatus.Reserved || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active))
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//Ruumly//Bookings//EN");
        sb.AppendLine("CALSCALE:GREGORIAN");
        foreach (var b in bookings)
        {
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{b.Id}@ruumly.eu");
            sb.AppendLine($"DTSTART;VALUE=DATE:{b.StartDate:yyyyMMdd}");
            if (b.EndDate.HasValue) sb.AppendLine($"DTEND;VALUE=DATE:{b.EndDate.Value:yyyyMMdd}");
            sb.AppendLine($"SUMMARY:{b.Listing?.Title ?? "Booking"} - {b.ContactName}");
            sb.AppendLine($"DESCRIPTION:Booking #{b.Id}\\nCustomer: {b.ContactName}\\n{b.ContactEmail}\\n{b.ContactPhone}");
            sb.AppendLine("STATUS:CONFIRMED");
            sb.AppendLine("END:VEVENT");
        }
        sb.AppendLine("END:VCALENDAR");

        return File(Encoding.UTF8.GetBytes(sb.ToString()),
            "text/calendar", "ruumly-calendar.ics");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid?> GetSupplierIdAsync(Guid userId)
    {
        // Admin: must pass ?supplierId= query param to act on behalf of a supplier
        if (User.IsInRole("Admin"))
        {
            if (Request.Query.TryGetValue("supplierId", out var sv) &&
                Guid.TryParse(sv, out var sid))
                return sid;
            return null;
        }

        return await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.SupplierId)
            .FirstOrDefaultAsync();
    }
}
