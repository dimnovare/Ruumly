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

    /// <summary>
    /// User's preferred language: "et", "en", or "ru".
    /// Defaults to Estonian.
    /// </summary>
    public string Language { get; set; } = "et";

    public string? Avatar { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public int BookingsCount { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiry { get; set; }

    public bool EmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationExpiry { get; set; }

    /// <summary>
    /// Google's stable subject identifier (sub claim).
    /// Null for email/password users. A user can have both if they
    /// later link their Google account.
    /// </summary>
    public string? GoogleId { get; set; }

    /// <summary>
    /// For Provider role users: the supplier they manage.
    /// Null for Customer and Admin roles.
    /// </summary>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public List<Booking> Bookings { get; set; } = [];
    public List<Message> Messages { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
