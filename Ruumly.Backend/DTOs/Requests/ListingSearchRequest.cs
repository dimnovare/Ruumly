using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Ruumly.Backend.DTOs.Requests;

public record ListingSearchRequest
{
    [FromQuery(Name = "type")]         public string?  Type         { get; init; }
    [FromQuery(Name = "city")]         [MaxLength(100)] public string?  City         { get; init; }
    [FromQuery(Name = "priceMax")]     public decimal? PriceMax     { get; init; }
    [FromQuery(Name = "sort")]         [MaxLength(30)]  public string?  Sort         { get; init; }
    [FromQuery(Name = "q")]            [MaxLength(200)] public string?  Q            { get; init; }
    [FromQuery(Name = "availableNow")] public bool?    AvailableNow { get; init; }
    // Date-availability window (yyyy-MM-dd): "free on these dates" — trailer (a rental
    // range) and moving (a service date). Excludes listings blacked out or at capacity.
    [FromQuery(Name = "availableFrom")] public string? AvailableFrom { get; init; }
    [FromQuery(Name = "availableTo")]   public string? AvailableTo   { get; init; }
    [FromQuery(Name = "country")]      public string?  Country      { get; init; }
    [FromQuery(Name = "minSize")]      public decimal? MinSize      { get; init; }
    [FromQuery(Name = "maxSize")]      public decimal? MaxSize      { get; init; }
    [FromQuery(Name = "sizeCategory")] public string?  SizeCategory { get; init; }
    [FromQuery(Name = "supplierId")]   public Guid?    SupplierId   { get; init; }
    [FromQuery(Name = "locationId")]   public Guid?    LocationId   { get; init; }
    // Browse-all / map / stats contexts (e.g. the homepage map) want EVERY active
    // listing, including those grouped under a real Location (which generic search
    // hides to avoid duplicate location-card + listing-card display). Opt-in bypass.
    [FromQuery(Name = "includeGrouped")] public bool?  IncludeGrouped { get; init; }
    [FromQuery(Name = "page")]         public int      Page         { get; init; } = 1;
    [FromQuery(Name = "limit")]        public int      Limit        { get; init; } = 50;
}
