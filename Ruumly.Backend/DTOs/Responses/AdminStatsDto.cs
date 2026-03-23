namespace Ruumly.Backend.DTOs.Responses;

public record AdminStatsDto(
    int     TotalListings,
    int     TotalOrders,
    int     TotalUsers,
    decimal TotalRevenue,
    int     OrdersThisMonth,
    decimal RevenueThisMonth,
    int     PendingOrders
);
