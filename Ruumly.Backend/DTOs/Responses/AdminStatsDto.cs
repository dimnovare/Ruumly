namespace Ruumly.Backend.DTOs.Responses;

public record AdminStatsDto(
    int     TotalListings,
    int     TotalOrders,
    int     TotalUsers,
    decimal TotalRevenue,
    int     OrdersThisMonth,
    decimal RevenueThisMonth,
    int     PendingOrders,
    IEnumerable<RecentInquiryDto> RecentInquiries
);

public record RecentInquiryDto(
    Guid   Id,
    string Customer,
    string Email,
    string Listing,
    string Type,
    string Date,
    string Status,
    string Notes
);
