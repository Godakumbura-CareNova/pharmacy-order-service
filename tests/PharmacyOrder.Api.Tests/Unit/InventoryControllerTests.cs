using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Inventory;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class InventoryControllerTests
{
    private readonly Mock<IInventoryService> _inventory = new();
    private InventoryController CreateController() => new(_inventory.Object);

    // 14 — walks every HTTP outcome the controller can produce
    [Fact]
    public async Task Controller_MapsServiceOutcomesToHttpResults()
    {
        var controller = CreateController();
        var request = new StockBatchRequestDto { MedicineId = 1, BatchNumber = "B-1", Quantity = 5 };
        var dto = new StockBatchResponseDto { Id = 7, MedicineId = 1, BatchNumber = "B-1", Quantity = 5 };

        _inventory.Setup(s => s.GetInventoryAsync(true)).ReturnsAsync(new[] { new MedicineInventoryResponseDto { MedicineId = 1, IsLowStock = true } });
        _inventory.Setup(s => s.GetBatchesAsync(1)).ReturnsAsync(new[] { dto });
        _inventory.Setup(s => s.CreateBatchAsync(request)).ReturnsAsync(dto);

        var list = Assert.IsType<OkObjectResult>((await controller.Get(lowStockOnly: true)).Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<MedicineInventoryResponseDto>>(list.Value));
        Assert.IsType<OkObjectResult>((await controller.Batches(1)).Result);
        var created = Assert.IsType<OkObjectResult>((await controller.Create(request)).Result);
        Assert.Equal(7, Assert.IsType<StockBatchResponseDto>(created.Value).Id);

        _inventory.Setup(s => s.UpdateBatchAsync(99, request)).ThrowsAsync(new KeyNotFoundException("Stock batch was not found."));
        Assert.IsType<NotFoundObjectResult>((await controller.Update(99, request)).Result);

        _inventory.Setup(s => s.UpdateBatchAsync(7, request)).ThrowsAsync(new ArgumentException("Stock quantity cannot be negative."));
        Assert.IsType<BadRequestObjectResult>((await controller.Update(7, request)).Result);

        _inventory.Setup(s => s.CreateBatchAsync(request)).ThrowsAsync(new InvalidOperationException("This batch number already exists for the medicine."));
        Assert.IsType<ConflictObjectResult>((await controller.Create(request)).Result);
    }
}
