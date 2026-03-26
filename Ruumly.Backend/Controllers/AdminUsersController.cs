using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs;
using Ruumly.Backend.DTOs.Requests;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Controllers;

[Route("api/admin")]
public class AdminUsersController(RuumlyDbContext db) : AdminBaseController(db)
{
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var total = await Db.Users.CountAsync();
        var users = await Db.Users
            .OrderByDescending(u => u.RegisteredAt)
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
        await Audit("user.status_changed", User.GetUserEmail(),
            user.Name, $"{prev} → {status}");
        await Db.SaveChangesAsync();

        return Ok(AdminMappers.MapUser(user));
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
        await Audit("user.supplier_assigned", User.GetUserEmail(),
            user.Name, $"Assigned to {supplier.Name}");
        await Db.SaveChangesAsync();

        return Ok(new { userId, supplierId, supplierName = supplier.Name });
    }
}
