namespace Ruumly.Backend.Models;

public class Review
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid ListingId { get; set; }
    public Listing Listing { get; set; } = null!;

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>1–5 star rating.</summary>
    public int Rating { get; set; }

    /// <summary>Optional free-text review, max 1 000 chars.</summary>
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }
}
