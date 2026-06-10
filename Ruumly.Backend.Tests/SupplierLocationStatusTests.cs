using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ruumly.Backend.Controllers;
using Ruumly.Backend.Data;
using Ruumly.Backend.DTOs.Responses;
using Ruumly.Backend.Models;

namespace Ruumly.Backend.Tests;

public class SupplierLocationStatusTests
{
    [Fact]
    public async Task GetById_ReturnsLocationPublishStatus()
    {
        var options = new DbContextOptionsBuilder<RuumlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new TestDbContext(options);
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Published Supplier",
            ContactName = "Test",
            ContactEmail = "supplier@example.com",
            ContactPhone = "+3725550000",
            IsActive = true,
        };
        var location = new SupplierLocation
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Supplier = supplier,
            Name = "Published Location",
            Address = "Test 1",
            City = "Tallinn",
            IsActive = true,
        };
        db.Suppliers.Add(supplier);
        db.SupplierLocations.Add(location);
        await db.SaveChangesAsync();

        var controller = new LocationsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.GetById(location.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<SupplierLocationDto>().Subject;
        dto.IsActive.Should().BeTrue();
    }
}
