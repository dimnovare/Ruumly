namespace Ruumly.Backend.DTOs.Responses;

public record SupplierProfileDto(
    Guid    Id,
    string  Slug,
    string  Name,
    string  Country,
    string? Tagline,
    Dictionary<string, string>? LongDescription,   // parsed from LongDescriptionTranslationsJson
    string? LogoUrl,
    string? HeroImageUrl,
    string? WebsiteUrl,
    int?    FoundedYear,
    decimal Rating,
    int     ReviewCount,
    string  Tier,
    bool    IsVerified,
    bool    FoundingPartner,
    int     LocationCount,
    int     ListingCount,
    List<SupplierProfileLocationDto> Locations,
    /// <summary>True when a Google Place ID is configured — lets the frontend decide whether to render the reviews section.</summary>
    bool    HasGoogleReviews,
    /// <summary>
    /// PROVENANCE only: this row was imported into the directory rather than
    /// hand-added. It decides how the profile renders. It does NOT say whether
    /// anybody owns the row or can be reached — see <see cref="RepliesDirectly"/>.
    /// The two were conflated once already, in ProviderCandidateFinder, where
    /// gating capability on provenance made every admin-added partner invisible
    /// to every lead.
    /// </summary>
    bool    IsDirectory = false,
    /// <summary>Service category slugs from Supplier.ServiceTypesJson; null/empty when none.</summary>
    List<string>? ServiceTypes = null,
    /// <summary>
    /// The provider's own headline "from" price and unit, set through the claim
    /// form. Null until they give us one — the page shows nothing rather than a
    /// zero or a "price on request" placeholder, because an empty space is
    /// honest and a fabricated placeholder is not.
    /// </summary>
    decimal? PriceFrom = null,
    string? PriceUnit = null,
    string? PriceNote = null,
    /// <summary>
    /// True when this partner has at least one provider login, and therefore
    /// actually receives a message sent from their profile page.
    ///
    /// The partner-page contact dialog promises, in five languages, that the
    /// partner will reply. For an unowned row nobody is on the other end, and
    /// two real people found that out by waiting — a Lithuanian jobseeker and an
    /// Estonian visitor on a Peetri self-storage page. The dialog now branches on
    /// this, NOT on <see cref="IsDirectory"/>: provenance and reachability are
    /// different questions, and a partner hand-added by an admin has
    /// IsDirectory=false while still having nobody to email.
    ///
    /// Defaults to FALSE deliberately. The profile is cached, so an entry written
    /// before this field existed deserializes without it — and the safe direction
    /// for a missing value is to promise nothing rather than to promise a reply
    /// we cannot deliver.
    /// </summary>
    bool    RepliesDirectly = false);

public record SupplierProfileLocationDto(
    Guid     Id,
    string   Name,
    string   Address,
    string   City,
    string   Country,
    double   Lat,
    double   Lng,
    string?  OpeningHours,
    string?  Description,
    List<string> Images,
    int      ListingCount,
    int?     TotalUnitCount,
    int?     AvailableUnitCount);
