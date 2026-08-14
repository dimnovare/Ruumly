using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;
using BC = BCrypt.Net.BCrypt;
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
                    hint    = "Pass ?supplierId=<guid> to act on behalf of a supplier team.",
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
    [EnableRateLimiting("user")]
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
                    hint    = "Pass ?supplierId=<guid> to act on behalf of a supplier team.",
                });
            return NotFound(new { message = "No supplier linked to this account." });
        }

        // Validate the invited identity before creating a User row / sending mail
        // (mirrors the email validation used elsewhere; prevents junk rows + wasted sends).
        if (string.IsNullOrWhiteSpace(request.Email)
            || request.Email.Length > 320
            || !System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
            return BadRequest(new { message = "A valid email address is required." });
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return BadRequest(new { message = "A name (max 200 characters) is required." });

        // Find or create the invited user, keyed on the canonical form of the address.
        // An invite typed with different capitalisation than the member's existing
        // account used to miss them here and mint a SECOND row for the same mailbox —
        // which then made the login they were sent a reset link for ambiguous.
        var email = EmailValidation.Normalize(request.Email);

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (existing is not null)
        {
            if (existing.SupplierId == supplierId)
                return Conflict(new { message = "User is already a member of this team." });

            if (existing.SupplierId.HasValue && existing.SupplierId != supplierId)
                return Conflict(new { message = "This user is already linked to another supplier." });

            existing.SupplierId = supplierId;
            existing.Role       = UserRole.Provider;
        }
        else
        {
            var newUser = new User
            {
                Id           = Guid.NewGuid(),
                Name         = request.Name,
                Email        = email,
                PasswordHash = BC.HashPassword(Guid.NewGuid().ToString(), workFactor: 4),
                Role         = UserRole.Provider,
                SupplierId   = supplierId,
                RegisteredAt = DateTime.UtcNow,
            };
            db.Users.Add(newUser);
        }

        await db.SaveChangesAsync();

        // Send password-reset email so the invited user can set their password
        await authService.RequestPasswordResetAsync(email);

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
                    hint    = "Pass ?supplierId=<guid> to act on behalf of a supplier team.",
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
                    hint    = "Pass ?supplierId=<guid> to act on behalf of a supplier team.",
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
        var listingCount  = await db.Listings.CountAsync(l => l.IsActive && l.SupplierId == supplier.Id);
        var dto           = AdminMappers.MapSupplier(
            supplier,
            ordersTotal:     ordersTotal,
            revenue:         revenue,
            listingCount:    listingCount,
            includeSettings: true,
            pricingConfig:   pricingConfig);

        return Ok(dto);
    }

    // PATCH /api/supplier/profile
    [HttpPatch("profile")]
    [Authorize(Roles = "Provider")]
    [EnableRateLimiting("user")]
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

        if (body.ContactName is not null) supplier.ContactName = body.ContactName;

        if (body.ContactEmail is not null)
        {
            var email = body.ContactEmail.Trim();
            if (email.Length > 0)
            {
                if (!email.Contains('@') || email.Length > 320 ||
                    !System.Net.Mail.MailAddress.TryCreate(email, out _))
                    return BadRequest(new { error = "Invalid contact email format." });
                supplier.ContactEmail = email;
            }
        }

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

        // Tier gate — non-admin providers on Starter tier cannot access analytics
        if (User.GetUserRole() != UserRole.Admin && effectiveSupplierId.HasValue)
        {
            var analyticsSupplier = await db.Suppliers.FindAsync(effectiveSupplierId.Value);
            var analyticsConfig   = await pricingConfigService.GetAsync();
            if (analyticsSupplier is not null &&
                !analyticsConfig.ForTier(analyticsSupplier.Tier).HasFullAnalytics)
                return Forbid();
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
                   (b.Status == BookingStatus.Reserved || (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.AwaitingConfirmation) || b.Status == BookingStatus.Active))
            .ToListAsync();

        // Strip CRLF/newlines from user-supplied iCal fields to prevent injection
        static string IcalSafe(string? value, int maxLen = 150)
        {
            var s = (value ?? string.Empty)
                .Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ")
                .Trim();
            return s[..Math.Min(s.Length, maxLen)];
        }

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
            sb.AppendLine($"SUMMARY:{IcalSafe(b.Listing?.Title ?? "Booking")} - {IcalSafe(b.ContactName)}");
            sb.AppendLine($"DESCRIPTION:Booking #{b.Id}\\nCustomer: {IcalSafe(b.ContactName)}\\n{IcalSafe(b.ContactEmail)}\\n{IcalSafe(b.ContactPhone)}");
            sb.AppendLine("STATUS:CONFIRMED");
            sb.AppendLine("END:VEVENT");
        }
        sb.AppendLine("END:VCALENDAR");

        return File(Encoding.UTF8.GetBytes(sb.ToString()),
            "text/calendar", "ruumly-calendar.ics");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid?> GetSupplierIdAsync(Guid userId, Guid? supplierIdOverride = null)
    {
        // Admin: optional direct override from caller, then fall back to ?supplierId= query param
        if (User.IsInRole("Admin"))
        {
            if (supplierIdOverride.HasValue) return supplierIdOverride;
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
