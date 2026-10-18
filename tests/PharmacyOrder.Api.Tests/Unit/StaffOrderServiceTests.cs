using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class StaffOrderServiceTests
{
    [Fact]
    public async Task Active_queue_returns_pending_and_ready_orders_but_not_completed_orders()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Orders.AddRange(
            new Order { Id = 1, CustomerId = Guid.NewGuid(), Status = OrderStatus.Pending, TotalAmount = 200m },
            new Order { Id = 2, CustomerId = Guid.NewGuid(), Status = OrderStatus.ReadyForCollection, TotalAmount = 300m },
            new Order { Id = 3, CustomerId = Guid.NewGuid(), Status = OrderStatus.Collected, TotalAmount = 400m });
        await db.SaveChangesAsync();

        var orders = await new StaffOrderService(db, new KafkaSettings()).ListActiveAsync();

        Assert.Equal(new[] { 2, 1 }, orders.Select(order => order.Id));
    }
}
