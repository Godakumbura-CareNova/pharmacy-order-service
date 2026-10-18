using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Inventory;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.UnitMocking.Tests;

// GCN-28 Stock Management and Low Stock Tracking
public class GCN28_StockManagementLowStockTests
{
    private static readonly KafkaSettings Settings = new() { LowStockThreshold = 10 };

    private static AppDbContext DbWithMedicine()
    {
        var db = TestDoubles.FakeDb();
        db.Medicines.Add(new Medicine { Id = 1, Name = "Panadol", Price = 50m });
        db.SaveChanges();
        return db;
    }

    private static StockBatchRequestDto Batch(int quantity, string batchNumber = "B-001") => new()
    {
        MedicineId = 1, BatchNumber = batchNumber, Quantity = quantity, PurchasePrice = 30m, SellingPrice = 50m,
        ManufacturingDate = new DateTime(2026, 1, 1), ExpiryDate = new DateTime(2027, 12, 31)
    };

    // stock level decides which inventory events are raised
    [Theory]
    [InlineData(10, new[] { EventTypes.InventoryUpdated, EventTypes.LowStockDetected })]
    public async Task CreateBatchAsync_RaisesEventsForStockLevel(int quantity, string[] expectedEvents)
    {
        var db = DbWithMedicine();

        await new InventoryService(db, Settings).CreateBatchAsync(Batch(quantity));

        Assert.Equal(quantity, db.Medicines.Single().StockQuantity);
        Assert.Equal(expectedEvents.OrderBy(e => e), db.EventOutboxEntries.Select(e => e.EventType).OrderBy(e => e));
    }

    // invalid batch details are rejected
    [Theory]
    [InlineData("B-001", 5, 2025)]
    public async Task CreateBatchAsync_InvalidBatch_IsRejected(string batchNumber, int quantity, int expiryYear)
    {
        var db = DbWithMedicine();
        var request = Batch(quantity, batchNumber);
        request.ExpiryDate = new DateTime(expiryYear, 6, 1);

        await Assert.ThrowsAsync<ArgumentException>(() => new InventoryService(db, Settings).CreateBatchAsync(request));

        Assert.Empty(db.Stocks);
    }

    // low stock list only shows medicines at or below the threshold
    [Fact]
    public async Task GetInventoryAsync_LowStockOnly_ReturnsLowAndOutOfStock()
    {
        var db = TestDoubles.FakeDb();
        db.Medicines.AddRange(
            new Medicine { Id = 1, Name = "Panadol", StockQuantity = 50 },
            new Medicine { Id = 2, Name = "Brufen", StockQuantity = 4 },
            new Medicine { Id = 3, Name = "Cetirizine", StockQuantity = 0 });
        db.SaveChanges();

        var lowStock = await new InventoryService(db, Settings).GetInventoryAsync(lowStockOnly: true);

        Assert.Equal(new[] { ("Brufen", "Low stock"), ("Cetirizine", "Out of stock") },
            lowStock.Select(item => (item.MedicineName, item.AvailabilityStatus)));
    }

    // controller passes the low stock filter
    [Fact]
    public async Task Get_LowStockOnly_PassesFilterToService()
    {
        var service = new Mock<IInventoryService>();
        service.Setup(s => s.GetInventoryAsync(true)).ReturnsAsync(new List<MedicineInventoryResponseDto>());

        var result = await new InventoryController(service.Object).Get(lowStockOnly: true);

        Assert.IsType<OkObjectResult>(result.Result);
        service.Verify(s => s.GetInventoryAsync(true), Times.Once);
        service.Verify(s => s.GetInventoryAsync(false), Times.Never);
    }

    // stock changed by someone else gives 409
    [Fact]
    public async Task Update_WhenStockChangedElsewhere_Returns409()
    {
        var service = new Mock<IInventoryService>();
        service.Setup(s => s.UpdateBatchAsync(5, It.IsAny<StockBatchRequestDto>())).ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await new InventoryController(service.Object).Update(5, Batch(20));

        Assert.IsType<ConflictObjectResult>(result.Result);
    }
}
