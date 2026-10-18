using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.UnitMocking.Tests;

// GCN-31 Checkout and Order Tracking
public class GCN31_CheckoutOrderTrackingTests
{
    private static readonly Guid PatientId = Guid.NewGuid();

    private readonly AppDbContext _db = TestDoubles.FakeDb();
    private readonly OrderService _sut;

    public GCN31_CheckoutOrderTrackingTests()
    {
        _db.Medicines.Add(new Medicine { Id = 1, Name = "Panadol", Price = 50m, StockQuantity = 20 });
        _db.Stocks.AddRange(
            new Stock { Id = 1, MedicineId = 1, BatchNumber = "LATE", Quantity = 10, ExpiryDate = DateTime.UtcNow.AddYears(2) },
            new Stock { Id = 2, MedicineId = 1, BatchNumber = "SOON", Quantity = 10, ExpiryDate = DateTime.UtcNow.AddMonths(3) });
        _db.Carts.Add(new Cart { CustomerId = PatientId, Items = { new CartItem { Id = 5, MedicineId = 1, Quantity = 4 } } });
        _db.SaveChanges();
        _sut = new OrderService(_db, new KafkaSettings { LowStockThreshold = 10 });
    }

    private static OrderRequestDto Checkout(Guid? requestId = null, decimal price = 50m) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        Items = { new CheckoutItemDto { CartItemId = 5, Quantity = 4, UnitPrice = price } }
    };

    // empty checkout is rejected
    [Theory]
    [InlineData(true, false)]
    public async Task PlaceAsync_EmptyCheckout_IsRejected(bool emptyRequestId, bool noItems)
    {
        var request = Checkout(emptyRequestId ? Guid.Empty : null);
        if (noItems) request.Items.Clear();

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.PlaceAsync(PatientId, request));

        Assert.Empty(_db.Orders);
    }

    // order is placed, stock taken from the batch expiring first, cart emptied
    [Fact]
    public async Task PlaceAsync_ValidCheckout_PlacesOrder_AndUsesEarliestExpiryBatch()
    {
        var order = await _sut.PlaceAsync(PatientId, Checkout());

        Assert.Equal(("Pending", 200m), (order.Status, order.TotalAmount));
        Assert.Equal(16, _db.Medicines.Single().StockQuantity);
        Assert.Equal(6, _db.Stocks.Single(s => s.BatchNumber == "SOON").Quantity);
        Assert.Empty(_db.CartItems);
        Assert.Contains(_db.EventOutboxEntries, e => e.EventType == EventTypes.OrderPlaced);
    }

    // same request twice creates only one order
    [Fact]
    public async Task PlaceAsync_SameRequestTwice_CreatesOneOrder()
    {
        var requestId = Guid.NewGuid();

        var first = await _sut.PlaceAsync(PatientId, Checkout(requestId));
        var second = await _sut.PlaceAsync(PatientId, Checkout(requestId));

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_db.Orders);
    }

    // changed price must be reviewed again
    [Fact]
    public async Task PlaceAsync_PriceChanged_IsRejected_AndStockUnchanged()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.PlaceAsync(PatientId, Checkout(price: 45m)));

        Assert.Equal(20, _db.Medicines.Single().StockQuantity);
        Assert.Empty(_db.Orders);
    }

    // patient can only track their own orders
    [Fact]
    public async Task GetAsync_OtherPatientsOrder_IsNotFound()
    {
        var order = await _sut.PlaceAsync(PatientId, Checkout());

        Assert.Equal($"CN-{order.Id:D6}", (await _sut.GetAsync(PatientId, order.Id)).OrderNumber);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.GetAsync(Guid.NewGuid(), order.Id));
    }
}
