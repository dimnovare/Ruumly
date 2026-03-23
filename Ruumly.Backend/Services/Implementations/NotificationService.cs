using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Models;
using Ruumly.Backend.Models.Enums;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Services.Implementations;

public class NotificationService(RuumlyDbContext db) : INotificationService
{
    public async Task<List<NotificationDto>> GetAllAsync(Guid userId)
    {
        var notifications = await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return notifications.Select(MapToDto).ToList();
    }

    public async Task MarkReadAsync(Guid id, Guid userId)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId)
            ?? throw new NotFoundException($"Notification {id} not found.");

        notification.Read = true;
        await db.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync(Guid userId)
    {
        await db.Notifications
            .Where(n => n.UserId == userId && !n.Read)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true));
    }

    public async Task CreateAsync(
        Guid             userId,
        NotificationType type,
        string           title,
        string           desc,
        string?          actionUrl  = null,
        string?          entityId   = null,
        string?          entityType = null)
    {
        db.Notifications.Add(new Notification
        {
            Id         = Guid.NewGuid(),
            UserId     = userId,
            Type       = type,
            Title      = title,
            Desc       = desc,
            Read       = false,
            ActionUrl  = actionUrl,
            EntityId   = entityId,
            EntityType = entityType,
            Channel    = NotificationChannel.InApp,
            CreatedAt  = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ─── Mapping ──────────────────────────────────────────────────────────────

    private static NotificationDto MapToDto(Notification n) => new(
        Id:         n.Id,
        Type:       n.Type.ToString().ToLower(),
        Title:      n.Title,
        Desc:       n.Desc,
        Read:       n.Read,
        Time:       ToRelativeTime(n.CreatedAt),
        ActionUrl:  n.ActionUrl,
        EntityId:   n.EntityId,
        EntityType: n.EntityType
    );

    private static string ToRelativeTime(DateTime createdAt)
    {
        var diff = DateTime.UtcNow - createdAt;

        if (diff.TotalMinutes < 1)   return "just now";
        if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes} minutit tagasi";
        if (diff.TotalHours   < 24)  return $"{(int)diff.TotalHours} tundi tagasi";
        if (diff.TotalDays    < 7)   return $"{(int)diff.TotalDays} päeva tagasi";
        return createdAt.ToLocalTime().ToString("dd.MM.yyyy");
    }
}
