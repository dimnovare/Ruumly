namespace Ruumly.Backend.DTOs.Requests;

public record CreateRoutingRuleRequest(
    string   Name,
    Guid?    SupplierId       = null,
    string?  ServiceType      = null,
    string?  OrderType        = null,
    decimal? PriceThreshold   = null,
    string?  CustomerType     = null,
    bool     RequiresApproval = false,
    string   ApproverRole     = "admin",
    string   PostingChannel   = "email",
    int      Priority         = 1,
    bool     IsActive         = true
);
