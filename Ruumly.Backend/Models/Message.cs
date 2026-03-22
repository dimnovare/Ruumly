using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public MessageSender From { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool Read { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string AttachmentsJson { get; set; } = "[]";

    [NotMapped]
    public List<string> Attachments
    {
        get => JsonSerializer.Deserialize<List<string>>(AttachmentsJson) ?? [];
        set => AttachmentsJson = JsonSerializer.Serialize(value);
    }
}
