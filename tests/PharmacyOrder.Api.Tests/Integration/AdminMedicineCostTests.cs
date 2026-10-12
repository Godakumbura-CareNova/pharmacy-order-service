using System.Net.Http.Json;
using System.Text.Json;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Tests.Integration;
public class AdminMedicineCostTests
{
    [Theory]
    [InlineData(null, 401)] [InlineData("Patient", 403)] [InlineData("Doctor", 403)] [InlineData("PharmacyStaff", 403)] [InlineData("Admin", 200)]
    public async Task OnlyAdmin(string? role, int status)
    {
        using var factory = new CartApiFactory(); using var client = factory.CreateClientAs(role);
        Assert.Equal(status, (int)(await client.GetAsync("/api/admin/reports/medicine-costs?orderIds=1")).StatusCode);
    }
    [Fact]
    public async Task CostsUseAllocatedBatchesAndMissingCostsAreNotZero()
    {
        using var factory = new CartApiFactory();
        var medicine = await factory.SeedMedicineAsync("Test", stock: 20);
        var medicineId = medicine.Id;
        await factory.QueryAsync(async db => {
            var batch = new Stock { MedicineId = medicineId, BatchNumber = "B1", Quantity = 10, PurchasePrice = 20, SellingPrice = 50 };
            db.Stocks.Add(batch);
            for (var i = 1; i <= 3; i++)
            {
                var order = new Order { Id = i, CustomerId = Guid.NewGuid(), RequestId = Guid.NewGuid(), TotalAmount = 150, Status = i == 3 ? OrderStatus.Rejected : OrderStatus.Collected };
                var line = new OrderItem { MedicineId = medicineId, Quantity = 3, UnitPrice = 50 };
                if (i != 2) line.Allocations.Add(new() { Stock = batch, Quantity = 3 });
                order.Items.Add(line); db.Orders.Add(order);
            }
            return await db.SaveChangesAsync();
        });
        using var client = factory.CreateClientAs("Admin");
        var response = await client.GetAsync("/api/admin/reports/medicine-costs?orderIds=1&orderIds=2&orderIds=3&orderIds=99");
        Assert.Equal(200, (int)response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(60, rows[0].GetProperty("buyingCost").GetDecimal());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("buyingCost").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[2].GetProperty("buyingCost").ValueKind);
        Assert.Equal(400, (int)(await client.GetAsync("/api/admin/reports/medicine-costs?orderIds=-1")).StatusCode);
    }
}
