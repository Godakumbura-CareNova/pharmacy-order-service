using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.UnitMocking;

// GCN-25 Medicine Catalogue Browsing, Search and Filtering
public class GCN25_MedicineCatalogueBrowsingTests
{
    private static CatalogueService ServiceWithMedicines()
    {
        var db = TestDoubles.FakeDb();
        var pain = new Category { Id = 1, Name = "Pain Relief" };
        var vitamins = new Category { Id = 2, Name = "Vitamins" };
        db.Medicines.AddRange(
            new Medicine { Id = 1, Name = "Panadol", Description = "Paracetamol tablets", Category = pain, Price = 50m, StockQuantity = 20 },
            new Medicine { Id = 2, Name = "Vitamin C", Description = "Immune support", Category = vitamins, Price = 300m, StockQuantity = 0 },
            new Medicine { Id = 3, Name = "Brufen", Description = "Ibuprofen for pain", Category = pain, Price = 120m, StockQuantity = 5 },
            new Medicine { Id = 4, Name = "Old Syrup", Description = "Discontinued", Category = pain, Price = 80m, StockQuantity = 9, IsActive = false });
        db.SaveChanges();
        return new CatalogueService(db);
    }

    // controller passes the query to the service
    [Fact]
    public async Task GetMedicines_PassesFiltersToService_AndReturnsOk()
    {
        var page = new PaginatedCatalogueResponseDto { TotalCount = 1 };
        var service = new Mock<ICatalogueService>();
        service.Setup(s => s.GetMedicinesAsync("pan", 1, true, 2, 12)).ReturnsAsync(page);

        var result = await new CatalogueController(service.Object).GetMedicines("pan", 1, true, 2, 12);

        Assert.Same(page, Assert.IsType<OkObjectResult>(result.Result).Value);
        service.Verify(s => s.GetMedicinesAsync("pan", 1, true, 2, 12), Times.Once);
    }

    // unknown medicine gives 404
    [Fact]
    public async Task GetMedicine_WhenServiceThrowsNotFound_Returns404()
    {
        var service = new Mock<ICatalogueService>();
        service.Setup(s => s.GetMedicineAsync(99)).ThrowsAsync(new KeyNotFoundException("Medicine is unavailable."));

        var result = await new CatalogueController(service.Object).GetMedicine(99);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // search by name or description, inactive medicines hidden
    [Theory]
    [InlineData("panadol", 1)]
    [InlineData("PAIN", 1)]
    [InlineData("ibuprofen", 1)]
    [InlineData("   ", 3)]
    public async Task GetMedicines_SearchTerm_FiltersResults(string search, int expectedCount)
    {
        var result = await ServiceWithMedicines().GetMedicinesAsync(search, null, null, 1, 12);

        Assert.Equal(expectedCount, result.TotalCount);
        Assert.DoesNotContain(result.Items, item => item.Name == "Old Syrup");
    }

    // in stock filter
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    [InlineData(null, 3)]
    public async Task GetMedicines_InStockFilter_FiltersResults(bool? inStock, int expectedCount)
    {
        var result = await ServiceWithMedicines().GetMedicinesAsync(null, null, inStock, 1, 12);

        Assert.Equal(expectedCount, result.TotalCount);
    }

    // paging values are kept in range
    [Theory]
    [InlineData(0, 50, 1, 12)]
    [InlineData(-2, 0, 1, 1)]
    [InlineData(2, 2, 2, 2)]
    public async Task GetMedicines_ClampsPaging(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var result = await ServiceWithMedicines().GetMedicinesAsync(null, null, null, page, pageSize);

        Assert.Equal((expectedPage, expectedPageSize), (result.Page, result.PageSize));
    }
}
