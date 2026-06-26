namespace Ruumly.Backend.DTOs.Responses;

public record ListingDto(
    Guid             Id,
    string           Type,          // "warehouse" | "moving" | "trailer"
    string           Title,
    string           SupplierName,
    string?          SupplierSlug,
    string           Address,
    string           City,
    double           Lat,
    double           Lng,
    decimal          PriceFrom,
    string           PriceUnit,
    bool             AvailableNow,
    string?          Badge,         // "cheapest" | "closest" | "best-value" | "promoted" | null
    decimal          Rating,
    int              ReviewCount,
    string           Description,
    List<string>     Images,
    Dictionary<string, object> Features,
    decimal?         PartnerDiscountRateOverride,
    decimal?         ClientDiscountRateOverride,
    decimal?         ClientDiscountRate,
    decimal?         EffectiveCustomerDiscount,
    decimal?         EffectivePartnerDiscount,
    decimal?         VatRate,
    bool             PricesIncludeVat,
    decimal?         DepositAmount,
    string?          RequiresLicenseCategory,
    int?             MinBookingMonths,
    Guid             SupplierId,
    decimal?         SizeM2,
    int?             QuantityTotal,
    Guid?            LocationId,
    int              ViewCount,
    bool             IsVerified,
    bool             FoundingPartner,
    bool             BookingEnabled,
    bool             ContractSigningEnabled,
    bool             DirectPaymentEnabled,
    bool             RuumlyPaymentEnabled,
    // True when an ACTIVE visibility boost (featured_search / featured_map /
    // service_area_boost / pickup_location_boost) currently applies to this listing.
    // Drives the "Featured" card treatment and the emphasised map pin on the frontend.
    bool             IsFeatured = false
);
