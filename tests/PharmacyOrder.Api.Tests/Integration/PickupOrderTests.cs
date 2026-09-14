using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Integration;

public class PickupOrderTests
{
    [Fact]
    public async Task Marking_an_order_ready_writes_one_status_event()
    {
        await using var fixture = await Fixture.Create();
        var order = await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request());

        await fixture.Service.TransitionAsync(order.Id, OrderStatus.ReadyForCollection, null);

        var entry = Assert.Single(await fixture.Db.EventOutboxEntries
            .Where(item => item.EventType == EventTypes.OrderReadyForCollection).ToListAsync());
        Assert.Equal(order.Id.ToString(), entry.EventKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_catalogue_and_inventory_notifications_remain_idempotent(bool inventory)
    {
        await using var fixture = await Fixture.Create();
        var eventId = Guid.NewGuid();
        var data = System.Text.Json.JsonSerializer.SerializeToElement(new { });
        var json = inventory
            ? System.Text.Json.JsonSerializer.Serialize(new PharmacyOrder.Api.Events.InventoryEvent(
                eventId, "InventoryUpdated", DateTime.UtcNow, 1, "Medicine", data))
            : System.Text.Json.JsonSerializer.Serialize(new PharmacyOrder.Api.Events.MedicineEvent(
                eventId, "MedicineCreated", DateTime.UtcNow, 1, "Medicine", data));
        var topic = inventory ? "carenova.inventory.events.v1" : "carenova.medicine.events.v1";
        var handler = new KafkaNotificationHandler(fixture.Db);
        await handler.HandleAsync(topic, json);
        await handler.HandleAsync(topic, json);
        var notification = Assert.Single(await fixture.Db.StaffNotifications.ToListAsync());
        Assert.Equal(eventId, notification.EventId);
        Assert.Equal("Medicine", notification.RelatedEntityType);
        Assert.Equal(1, notification.RelatedEntityId);
    }

    [Fact]
    public async Task Placement_queues_one_order_event_even_when_request_is_retried()
    {
        await using var fixture = await Fixture.Create();
        var request = fixture.Request();
        var order = await fixture.Service.PlaceAsync(fixture.Patient, request);
        await fixture.Service.PlaceAsync(fixture.Patient, request);
        var entry = Assert.Single(await fixture.Db.EventOutboxEntries
            .Where(e => e.EventType == "OrderPlaced").ToListAsync());
        Assert.Equal("carenova.order.events.v1", entry.Topic);
        Assert.Equal(order.Id.ToString(), entry.EventKey);
        using var json = System.Text.Json.JsonDocument.Parse(entry.PayloadJson);
        Assert.Equal(order.Id, json.RootElement.GetProperty("AggregateId").GetInt32());
        Assert.Equal("Pending", json.RootElement.GetProperty("Data").GetProperty("Status").GetString());
        Assert.DoesNotContain(fixture.Patient.ToString(), entry.PayloadJson);
        Assert.Null(entry.PublishedAtUtc);
        var handler = new KafkaNotificationHandler(fixture.Db);
        await handler.HandleAsync(entry.Topic, entry.PayloadJson);
        await handler.HandleAsync(entry.Topic, entry.PayloadJson);
        var notification = Assert.Single(await fixture.Db.StaffNotifications.ToListAsync());
        Assert.Equal(entry.EventId, notification.EventId);
        Assert.Equal(order.Id, notification.RelatedEntityId);
        Assert.Equal("New order received", notification.Title);
        Assert.Contains("pending pharmacy review", notification.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failure_after_saving_rolls_back_order_stock_cart_and_events(int failOnSave)
    {
        await using var fixture = await Fixture.Create();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(fixture.Db.Database.GetDbConnection())
            .AddInterceptors(new FailAfterSave(failOnSave)).Options;
        await using var failingDb = new AppDbContext(options);
        await Assert.ThrowsAsync<IOException>(() => new OrderService(failingDb, new KafkaSettings())
            .PlaceAsync(fixture.Patient, fixture.Request()));
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
        Assert.Empty(await fixture.Db.EventOutboxEntries.ToListAsync());
        Assert.Equal(2, await fixture.Db.CartItems.CountAsync());
        Assert.Equal(10, (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity);
        Assert.Equal(2, (await fixture.Db.Stocks.FindAsync(1))!.Quantity);
        Assert.Equal(8, (await fixture.Db.Stocks.FindAsync(2))!.Quantity);
    }

    private sealed class FailAfterSave(int failOnSave) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int saves;
        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (++saves == failOnSave) throw new IOException("Simulated failure before commit");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Retry_accepts_equivalent_decimal_scale()
    {
        await using var fixture = await Fixture.Create();
        var request = fixture.Request();
        var placed = await fixture.Service.PlaceAsync(fixture.Patient, request);
        request.Items[0].UnitPrice = 200.00m;
        var retry = await fixture.Service.PlaceAsync(fixture.Patient, request);
        Assert.Equal(placed.Id, retry.Id);
    }

    [Fact]
    public async Task Stale_no_op_batch_edit_cannot_resurrect_checkout_stock()
    {
        await using var fixture = await Fixture.Create();
        await using var staleDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(fixture.Db.Database.GetDbConnection()).Options);
        var stale = (await staleDb.Stocks.FindAsync(1))!;
        await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request());
        var inventory = new InventoryService(staleDb, new KafkaSettings());
        var request = new PharmacyOrder.Api.DTOs.Inventory.StockBatchRequestDto
        {
            MedicineId = stale.MedicineId, BatchNumber = stale.BatchNumber, Quantity = stale.Quantity,
            PurchasePrice = stale.PurchasePrice, SellingPrice = stale.SellingPrice,
            ManufacturingDate = stale.ManufacturingDate, ExpiryDate = stale.ExpiryDate
        };
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => inventory.UpdateBatchAsync(stale.Id, request));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity);
        Assert.Equal(0, (await fixture.Db.Stocks.FindAsync(1))!.Quantity);
    }

    [Fact]
    public async Task Placement_deducts_earliest_batches_and_preserves_unselected_pending_items()
    {
        await using var fixture = await Fixture.Create();
        var order = await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request());
        Assert.Equal("Pending", order.Status);
        Assert.Equal(600m, order.TotalAmount);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity);
        Assert.Equal(0, (await fixture.Db.Stocks.FindAsync(1))!.Quantity);
        Assert.Equal(7, (await fixture.Db.Stocks.FindAsync(2))!.Quantity);
        Assert.Null(await fixture.Db.CartItems.FindAsync(1));
        Assert.NotNull(await fixture.Db.CartItems.FindAsync(2));
        Assert.True(await fixture.Db.EventOutboxEntries.AnyAsync());
    }

    [Fact]
    public async Task Same_retry_returns_existing_order_without_deducting_again()
    {
        await using var fixture = await Fixture.Create();
        var request = fixture.Request();
        var first = await fixture.Service.PlaceAsync(fixture.Patient, request);
        var retry = await fixture.Service.PlaceAsync(fixture.Patient, request);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity);
        request.Items[0].Quantity = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PlaceAsync(fixture.Patient, request));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changed_review_values_block_placement(bool changeQuantity)
    {
        await using var fixture = await Fixture.Create();
        var request = fixture.Request();
        if (changeQuantity) request.Items[0].Quantity = 2;
        else request.Items[0].UnitPrice = 199m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PlaceAsync(fixture.Patient, request));
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
        Assert.Equal(2, await fixture.Db.CartItems.CountAsync());
    }

    [Fact]
    public async Task Empty_selection_and_missing_prescription_are_rejected()
    {
        await using var fixture = await Fixture.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PlaceAsync(fixture.Patient,
            new OrderRequestDto { RequestId = Guid.NewGuid(), Items = new() }));
        var item = (await fixture.Db.CartItems.FindAsync(2))!;
        item.PrescriptionId = null;
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PlaceAsync(fixture.Patient, fixture.Request(2, 2, 300m)));
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Pending_prescription_cannot_be_submitted_for_checkout()
    {
        await using var fixture = await Fixture.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PlaceAsync(fixture.Patient, fixture.Request(2, 2, 300m)));
    }

    [Fact]
    public async Task Verified_owned_prescription_can_be_ordered()
    {
        await using var fixture = await Fixture.Create();
        var prescription = (await fixture.Db.Prescriptions.FindAsync(1))!;
        prescription.Status = "Verified";
        prescription.IsVerified = true;
        await fixture.Db.SaveChangesAsync();
        var order = await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request(2, 2, 300m));
        Assert.Equal(600m, order.TotalAmount);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(8, (await fixture.Db.Medicines.FindAsync(2))!.StockQuantity);
    }

    [Fact]
    public async Task Another_patient_cannot_order_cart_items_or_read_order()
    {
        await using var fixture = await Fixture.Create();
        var stranger = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.PlaceAsync(stranger, fixture.Request()));
        var order = await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.GetAsync(stranger, order.Id));
        Assert.Empty(await fixture.Service.ListAsync(stranger));
    }

    [Fact]
    public async Task Rejection_restores_stock_once_and_collection_does_not_deduct_again()
    {
        await using var fixture = await Fixture.Create();
        var order = await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request());
        await fixture.Service.TransitionAsync(order.Id, OrderStatus.Rejected, "Cannot fulfil");
        await fixture.Service.TransitionAsync(order.Id, OrderStatus.Rejected, "Cannot fulfil");
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(10, (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity);
        Assert.Equal(2, (await fixture.Db.Stocks.FindAsync(1))!.Quantity);
        Assert.Equal(8, (await fixture.Db.Stocks.FindAsync(2))!.Quantity);
    }

    [Fact]
    public async Task Collection_does_not_deduct_again_and_cannot_be_rejected()
    {
        await using var fixture = await Fixture.Create();
        var order = await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request());
        await fixture.Service.TransitionAsync(order.Id, OrderStatus.ReadyForCollection, null);
        await fixture.Service.TransitionAsync(order.Id, OrderStatus.Collected, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.TransitionAsync(order.Id, OrderStatus.Rejected, "Too late"));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(7, (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Insufficient_stock_rolls_back_order_cart_and_outbox(bool insufficientAggregate)
    {
        await using var fixture = await Fixture.Create();
        if (insufficientAggregate) (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity = 1;
        else (await fixture.Db.Stocks.FindAsync(2))!.ExpiryDate = DateTime.UtcNow.AddDays(-1);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PlaceAsync(fixture.Patient, fixture.Request()));
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
        Assert.Empty(await fixture.Db.EventOutboxEntries.ToListAsync());
        Assert.Equal(2, await fixture.Db.CartItems.CountAsync());
        Assert.Equal(2, (await fixture.Db.Stocks.FindAsync(1))!.Quantity);
        Assert.Equal(8, (await fixture.Db.Stocks.FindAsync(2))!.Quantity);
    }

    [Fact]
    public async Task Order_history_keeps_original_name_and_price_after_catalogue_edit()
    {
        await using var fixture = await Fixture.Create();
        var placed = await fixture.Service.PlaceAsync(fixture.Patient, fixture.Request());
        fixture.Db.ChangeTracker.Clear();
        var medicine = (await fixture.Db.Medicines.FindAsync(1))!;
        medicine.Name = "Renamed medicine";
        medicine.Price = 999m;
        await fixture.Db.SaveChangesAsync();
        var history = await fixture.Service.GetAsync(fixture.Patient, placed.Id);
        Assert.Equal(DateTimeKind.Utc, history.OrderDate.Kind);
        var item = Assert.Single(history.Items);
        Assert.Equal("Ibuprofen", item.MedicineName);
        Assert.Equal(200m, item.UnitPrice);
        Assert.Equal(600m, history.TotalAmount);
        Assert.Single(await fixture.Service.ListAsync(fixture.Patient));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Db { get; }
        public Guid Patient { get; } = Guid.NewGuid();
        public OrderService Service => new(Db, new KafkaSettings());
        private Fixture(SqliteConnection connection)
        {
            this.connection = connection;
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        }
        public OrderRequestDto Request(int itemId = 1, int quantity = 3, decimal price = 200m) => new()
        {
            RequestId = Guid.NewGuid(),
            Items = new() { new CheckoutItemDto { CartItemId = itemId, Quantity = quantity, UnitPrice = price } }
        };
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.Db.Categories.Add(new Category { Id = 1, Name = "Test" });
            fixture.Db.Medicines.AddRange(
                new Medicine { Id = 1, CategoryId = 1, Name = "Ibuprofen", Price = 200m, StockQuantity = 10 },
                new Medicine { Id = 2, CategoryId = 1, Name = "Amoxicillin", Price = 300m, StockQuantity = 10, PrescriptionRequired = true });
            fixture.Db.Stocks.AddRange(
                new Stock { Id = 1, MedicineId = 1, BatchNumber = "EARLY", Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(10) },
                new Stock { Id = 2, MedicineId = 1, BatchNumber = "LATER", Quantity = 8, ExpiryDate = DateTime.UtcNow.AddDays(30) },
                new Stock { Id = 3, MedicineId = 2, BatchNumber = "RX", Quantity = 10, ExpiryDate = DateTime.UtcNow.AddDays(30) });
            fixture.Db.Prescriptions.Add(new Prescription { Id = 1, UploadedBy = fixture.Patient, MedicineId = 2, Quantity = 2, Status = "PendingVerification" });
            fixture.Db.Carts.Add(new Cart { Id = 1, CustomerId = fixture.Patient });
            fixture.Db.CartItems.AddRange(
                new CartItem { Id = 1, CartId = 1, MedicineId = 1, Quantity = 3 },
                new CartItem { Id = 2, CartId = 1, MedicineId = 2, Quantity = 2, PrescriptionId = 1 });
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();
            return fixture;
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
