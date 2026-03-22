using Ruumly.Backend.Models.Enums;

namespace Ruumly.Backend.Models;

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Customer;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string? Company { get; set; }
    public string? Phone { get; set; }
    public string? Avatar { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public int BookingsCount { get; set; }

    public List<Booking> Bookings { get; set; } = [];
    public List<Message> Messages { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
