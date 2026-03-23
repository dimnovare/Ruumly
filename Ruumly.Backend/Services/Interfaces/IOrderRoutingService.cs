using Ruumly.Backend.Models;

namespace Ruumly.Backend.Services.Interfaces;

public interface IOrderRoutingService
{
    Task RouteOrderAsync(Booking booking, Listing listing);
}
