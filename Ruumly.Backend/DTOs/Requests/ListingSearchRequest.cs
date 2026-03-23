using Microsoft.AspNetCore.Mvc;

namespace Ruumly.Backend.DTOs.Requests;

public record ListingSearchRequest
{
    [FromQuery(Name = "type")]         public string?  Type         { get; init; }
    [FromQuery(Name = "city")]         public string?  City         { get; init; }
    [FromQuery(Name = "priceMax")]     public decimal? PriceMax     { get; init; }
    [FromQuery(Name = "sort")]         public string?  Sort         { get; init; }
    [FromQuery(Name = "q")]            public string?  Q            { get; init; }
    [FromQuery(Name = "availableNow")] public bool?    AvailableNow { get; init; }
    [FromQuery(Name = "page")]         public int      Page         { get; init; } = 1;
    [FromQuery(Name = "limit")]        public int      Limit        { get; init; } = 50;
}
