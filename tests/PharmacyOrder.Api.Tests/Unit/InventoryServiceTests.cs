using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Inventory;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class InventoryServiceTests
{
    [Fact]
    public async Task CreateBatchAsync_RecalculatesTotalAndQueuesLowStockEvents()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var medicine = new Medicine { Name = "Paracetamol", Description = "Pain relief", IsActive = true };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        var service = new InventoryService(db, new Config.KafkaSettings { LowStockThreshold = 10 });

        await service.CreateBatchAsync(new StockBatchRequestDto
        {
            MedicineId = medicine.Id, BatchNumber = "PAR-001", Quantity = 8,
            PurchasePrice = 80m, SellingPrice = 100m,
            ManufacturingDate = new DateTime(2026, 1, 1), ExpiryDate = new DateTime(2027, 1, 1)
        });

        Assert.Equal(8, medicine.StockQuantity);
        Assert.Contains(db.EventOutboxEntries, entry => entry.EventType == EventTypes.InventoryUpdated);
        Assert.Contains(db.EventOutboxEntries, entry => entry.EventType == EventTypes.LowStockDetected);
    }
}
