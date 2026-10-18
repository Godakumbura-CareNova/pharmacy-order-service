using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.UnitMocking.Tests;

// GCN-26 Medicine Details and Quantity Selection
public class GCN26_MedicineDetailsQuantitySelectionTests
{
    private static readonly Guid PatientId = Guid.NewGuid();

    private static AppDbContext DbWithMedicine(int stock, bool active = true)
    {
        var db = TestDoubles.FakeDb();
        db.Medicines.Add(new Medicine
        {
            Id = 7, Name = "Panadol", Description = "Paracetamol 500mg", Price = 50m, StockQuantity = stock, IsActive = active,
            Category = new Category { Id = 1, Name = "Pain Relief" }
        });
        db.SaveChanges();
        return db;
    }

    // details page shows medicine information
    [Fact]
    public async Task GetMedicineAsync_ReturnsDetails_WithStockAsQuantityLimit()
    {
        var details = await new CatalogueService(DbWithMedicine(stock: 15)).GetMedicineAsync(7);

        Assert.Equal(("Panadol", "Paracetamol 500mg", "Pain Relief", 50m), (details.Name, details.Description, details.CategoryName, details.Price));
        Assert.Equal(15, details.StockQuantity);
    }

    // availability label depends on stock
    [Theory]
    [InlineData(0, "Out of stock")]
    public async Task GetMedicineAsync_ShowsAvailability(int stock, string expected)
    {
        var details = await new CatalogueService(DbWithMedicine(stock)).GetMedicineAsync(7);

        Assert.Equal(expected, details.AvailabilityStatus);
    }

    // inactive or missing medicine cannot be viewed
    [Theory]
    [InlineData(7, false)]
    public async Task GetMedicineAsync_InactiveOrMissing_Throws(int id, bool active)
    {
        var service = new CatalogueService(DbWithMedicine(stock: 5, active));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetMedicineAsync(id));
    }

    // selected quantity must be between 1 and stock
    [Theory]
    [InlineData(6)]
    public async Task AddToCart_QuantityOutsideStock_IsRejected(int quantity)
    {
        var db = DbWithMedicine(stock: 5);
        var cart = new CartService(db, Mock.Of<IPrescriptionCleanupService>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            cart.AddNonPrescriptionItemAsync(PatientId, new AddCartItemRequestDto { MedicineId = 7, Quantity = quantity }, CancellationToken.None));

        Assert.Empty(db.CartItems);
    }

    // controller asks the service once for the details
    [Fact]
    public async Task GetMedicine_ReturnsStubbedDetails()
    {
        var details = new CatalogueMedicineResponseDto { Id = 7, Name = "Panadol", StockQuantity = 15 };
        var service = new Mock<ICatalogueService>();
        service.Setup(s => s.GetMedicineAsync(7)).ReturnsAsync(details);

        var result = await new CatalogueController(service.Object).GetMedicine(7);

        Assert.Same(details, Assert.IsType<OkObjectResult>(result.Result).Value);
        service.Verify(s => s.GetMedicineAsync(7), Times.Once);
    }
}
