using Ruumly.Backend.Models;

namespace Ruumly.Backend.Services.Interfaces;

public interface IIntegrationDispatchService
{
    Task DispatchAsync(Order order, Supplier supplier);
}
