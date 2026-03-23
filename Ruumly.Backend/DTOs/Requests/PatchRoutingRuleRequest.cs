namespace Ruumly.Backend.DTOs.Requests;

public record PatchRoutingRuleRequest(
    string?  Name             = null,
    string?  ServiceType      = null,
    string?  OrderType        = null,
    decimal? PriceThreshold   = null,
    string?  CustomerType     = null,
    bool?    RequiresApproval = null,
    string?  ApproverRole     = null,
    string?  PostingChannel   = null,
    int?     Priority         = null,
    bool?    IsActive         = null
);
