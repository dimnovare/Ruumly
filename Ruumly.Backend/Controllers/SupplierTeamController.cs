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

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/supplier")]
[Authorize(Roles = "Provider,Admin")]
public class SupplierTeamController(RuumlyDbContext db, IAuthService authService) : ControllerBase
{
    // GET /api/supplier/team
    [HttpGet("team")]
    public async Task<IActionResult> GetTeam()
    {
        var userId     = User.GetUserId();
        var supplierId = await GetSupplierIdAsync(userId);
        if (supplierId is null)
            return NotFound(new { message = "No supplier linked to this account." });

        var owner = await db.Users
            .Where(u => u.SupplierId == supplierId && u.Role == UserRole.Provider)
            .OrderBy(u => u.RegisteredAt)
            .FirstOrDefaultAsync();

        var members = await db.Users
            .Where(u => u.SupplierId == supplierId && u.Role == UserRole.Provider)
            .OrderBy(u => u.RegisteredAt)
            .Select(u => new TeamMemberDto(
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Role.ToString(),
                u.Id == owner!.Id,
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
            return NotFound(new { message = "No supplier linked to this account." });

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
            return NotFound(new { message = "No supplier linked to this account." });

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
        var userId = User.GetUserId();
        var user   = await db.Users
            .Include(u => u.Supplier)
                .ThenInclude(s => s!.IntegrationSettings)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.Supplier is null)
            return NotFound(new { message = "No supplier profile linked to this account." });

        var s      = user.Supplier;
        var orders = await db.Orders.Where(o => o.SupplierId == s.Id).ToListAsync();
        var dto    = AdminMappers.MapSupplier(
            s,
            ordersTotal: orders.Count,
            revenue:     orders.Sum(o => o.Total),
            includeSettings: true);

        return Ok(dto);
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
