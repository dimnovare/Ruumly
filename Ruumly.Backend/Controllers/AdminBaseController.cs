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

    /// <summary>
    /// Queues an audit log entry in the current EF change tracker.
    /// Callers MUST call SaveChangesAsync() afterwards — no save happens here.
    /// This ensures the entity change and the audit log are committed atomically
    /// in a single round trip.
    /// </summary>
    protected void Audit(string action, string actor, string target, string? detail)
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
    }

    protected static object Error(string message) => AdminMappers.Error(message);
}
