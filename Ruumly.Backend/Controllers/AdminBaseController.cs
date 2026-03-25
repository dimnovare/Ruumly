using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Data;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public abstract class AdminBaseController(RuumlyDbContext db) : ControllerBase
{
    protected RuumlyDbContext Db { get; } = db;

    protected async Task Audit(string action, string actor, string target, string? detail)
    {
        Db.AuditLogs.Add(new AuditLog
        {
            Id        = Guid.NewGuid(),
            Action    = action,
            Actor     = actor,
            Target    = target,
            Detail    = detail,
            CreatedAt = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    protected static object Error(string message) => AdminMappers.Error(message);
}
