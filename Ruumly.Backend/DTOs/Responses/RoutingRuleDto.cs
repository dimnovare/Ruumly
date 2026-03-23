namespace Ruumly.Backend.DTOs.Responses;

public record RoutingRuleDto(
    Guid     Id,
    string   Name,
    Guid?    SupplierId,
    string?  ServiceType,
    string?  OrderType,
    decimal? PriceThreshold,
    string?  CustomerType,
    bool     RequiresApproval,
    string   ApproverRole,
    string   PostingChannel,
    int      Priority,
    bool     IsActive,
    string   CreatedAt,
    string   UpdatedAt
);
