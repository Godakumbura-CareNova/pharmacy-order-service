using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using System.Data.Common;

namespace PharmacyOrder.Api.Tests.Integration;

public class PickupOrderConcurrencyTests
{
    [Fact]
    public async Task Two_patients_competing_for_last_unit_create_only_one_order()
    {
        var file = Path.Combine(Path.GetTempPath(), $"carenova-checkout-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = file, Pooling = false, DefaultTimeout = 5 }.ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
        var firstPatient = Guid.NewGuid();
        var secondPatient = Guid.NewGuid();
        try
        {
            await using (var seed = new AppDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Categories.Add(new Category { Id = 1, Name = "Test" });
                seed.Medicines.Add(new Medicine { Id = 1, CategoryId = 1, Name = "Last unit", Price = 100, StockQuantity = 1 });
                seed.Stocks.Add(new Stock { Id = 1, MedicineId = 1, BatchNumber = "LAST", Quantity = 1, ExpiryDate = DateTime.UtcNow.AddDays(10) });
                seed.Carts.AddRange(new Cart { Id = 1, CustomerId = firstPatient }, new Cart { Id = 2, CustomerId = secondPatient });
                seed.CartItems.AddRange(new CartItem { Id = 1, CartId = 1, MedicineId = 1, Quantity = 1 },
                    new CartItem { Id = 2, CartId = 2, MedicineId = 1, Quantity = 1 });
                await seed.SaveChangesAsync();
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Checkout(Guid patient, int itemId)
            {
                await start.Task;
                await using var db = new AppDbContext(options);
                try
                {
                    await new OrderService(db, new KafkaSettings()).PlaceAsync(patient, new OrderRequestDto
                    {
                        RequestId = Guid.NewGuid(),
                        Items = new() { new CheckoutItemDto { CartItemId = itemId, Quantity = 1, UnitPrice = 100 } }
                    });
                    return true;
                }
                catch (Exception error) when (error is InvalidOperationException or DbException or DbUpdateException) { return false; }
            }
            var attempts = new[] { Task.Run(() => Checkout(firstPatient, 1)), Task.Run(() => Checkout(secondPatient, 2)) };
            start.SetResult();
            var results = await Task.WhenAll(attempts);
            Assert.Equal(1, results.Count(result => result));
            await using var verify = new AppDbContext(options);
            Assert.Equal(1, await verify.Orders.CountAsync());
            Assert.Equal(1, await verify.CartItems.CountAsync());
            Assert.Equal(0, (await verify.Medicines.SingleAsync()).StockQuantity);
            Assert.Equal(0, (await verify.Stocks.SingleAsync()).Quantity);
        }
        finally { File.Delete(file); }
    }
}
