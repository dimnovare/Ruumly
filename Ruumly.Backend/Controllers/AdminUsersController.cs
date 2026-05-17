using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminUsersController(RuumlyDbContext db) : AdminBaseController(db)
{
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int     page   = 1,
        [FromQuery] int     limit  = 50,
        [FromQuery] string? q      = null,
        [FromQuery] string? role   = null,
        [FromQuery] string? status = null)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);

        IQueryable<User> query = Db.Users.OrderByDescending(u => u.RegisteredAt);

        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            if (q.Length > 200) q = q[..200];
            var lq = q.ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(lq) ||
                u.Name.ToLower().Contains(lq));
        }

        if (!string.IsNullOrWhiteSpace(role) &&
            Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
            query = query.Where(u => u.Role == parsedRole);

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<UserStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(u => u.Status == parsedStatus);

        var total = await query.CountAsync();
        var users = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var data = users.Select(AdminMappers.MapUser).ToList();
        return Ok(new PaginatedResult<DTOs.Responses.UserDto>(
            data, total, page, limit,
            (page - 1) * limit + data.Count < total));
    }

    [HttpGet("users/{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var user = await Db.Users.FindAsync(id);
        if (user is null) return NotFound(Error("User not found"));
        return Ok(AdminMappers.MapUser(user));
    }

    [HttpPatch("users/{id:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(Guid id, [FromBody] UpdateUserStatusRequest body)
    {
        if (!Enum.TryParse<UserStatus>(body.Status, ignoreCase: true, out var status))
            return BadRequest(Error($"Invalid status '{body.Status}'. Use 'active' or 'blocked'."));

        var user = await Db.Users.FindAsync(id);
        if (user is null) return NotFound(Error("User not found"));

        var prev = user.Status;
        user.Status = status;
        Audit("user.status_changed", User.GetUserEmail(),
            user.Name, $"{prev} → {status}");
        await Db.SaveChangesAsync();

        return Ok(AdminMappers.MapUser(user));
    }

    [HttpPatch("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] AdminPatchUserRequest body)
    {
        var user = await Db.Users.FindAsync(id);
        if (user is null) return NotFound(new { error = "User not found" });

        if (body.Name    is not null) user.Name    = body.Name.Trim();
        if (body.Phone   is not null) user.Phone   = body.Phone.Trim();
        if (body.Company is not null) user.Company = body.Company.Trim();

        if (body.Role is not null && Enum.TryParse<UserRole>(body.Role, ignoreCase: true, out var newRole))
            user.Role = newRole;

        if (body.Status == "blocked")      user.Status = UserStatus.Blocked;
        else if (body.Status == "active")  user.Status = UserStatus.Active;

        if (body.EmailVerified.HasValue)
            user.EmailVerified = body.EmailVerified.Value;

        if (body.SupplierId.HasValue)
        {
            var newSupplierId = body.SupplierId.Value == Guid.Empty ? (Guid?)null : body.SupplierId.Value;
            if (newSupplierId.HasValue)
            {
                var supplierExists = await Db.Suppliers.AnyAsync(s => s.Id == newSupplierId.Value);
                if (!supplierExists)
                    return NotFound(Error($"Supplier {newSupplierId} not found."));
            }
            user.SupplierId = newSupplierId;
        }

        Audit("user.updated", User.GetUserEmail(), user.Name, null);
        await Db.SaveChangesAsync();

        return Ok(new
        {
            user.Id, user.Name, user.Email, user.Phone, user.Company,
            role           = user.Role.ToString().ToLower(),
            status         = user.Status.ToString().ToLower(),
            user.EmailVerified,
            supplierId     = user.SupplierId,
        });
    }

    [HttpPatch("users/{userId:guid}/assign-supplier/{supplierId:guid}")]
    public async Task<IActionResult> AssignSupplier(Guid userId, Guid supplierId)
    {
        var user = await Db.Users.FindAsync(userId);
        if (user is null) return NotFound(Error("User not found"));
        if (user.Role != UserRole.Provider)
            return BadRequest(Error("User must have Provider role"));

        var supplier = await Db.Suppliers.FindAsync(supplierId);
        if (supplier is null) return NotFound(Error("Supplier not found"));

        user.SupplierId = supplierId;
        Audit("user.supplier_assigned", User.GetUserEmail(),
            user.Name, $"Assigned to {supplier.Name}");
        await Db.SaveChangesAsync();

        return Ok(new { userId, supplierId, supplierName = supplier.Name });
    }
}
