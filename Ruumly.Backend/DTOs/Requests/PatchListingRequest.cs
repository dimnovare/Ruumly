namespace Ruumly.Backend.DTOs.Requests;

public record PatchListingRequest(
    string?       Title,
    string?       Description,
    decimal?      PriceFrom,
    string?       PriceUnit,
    decimal?      SizeM2,
    int?          QuantityTotal,
    decimal?      VatRate,
    bool?         IsActive,
    List<string>? Images,
    decimal?      DepositAmount            = null,
    string?       RequiresLicenseCategory  = null,
    int?          MinBookingMonths         = null,
    Dictionary<string, object>?  Features                 = null,
    Dictionary<string, string>?  DescriptionTranslations  = null
);
