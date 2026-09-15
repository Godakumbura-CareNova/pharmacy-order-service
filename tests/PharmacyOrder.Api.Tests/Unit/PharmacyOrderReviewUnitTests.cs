using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>
/// GCN-32 pharmacy order review — unit tests.
/// Covers staff order queue mapping, staff request validation, prescription queue filtering
/// and the patient-notification branch of the Kafka handler, using the EF in-memory provider.
/// </summary>
public class PharmacyOrderReviewUnitTests
{
    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StaffOrderService Orders(AppDbContext db) => new(db, new KafkaSettings());

    private static StaffPrescriptionService Prescriptions(AppDbContext db) => new(db, Mock.Of<IFileStorageService>());

    private static string OrderEventJson(Guid eventId, string type, int orderId, string entityType = "Order") =>
        JsonSerializer.Serialize(new OrderEvent(eventId, type, DateTime.UtcNow, orderId, entityType,
            JsonSerializer.SerializeToElement(new { })));

    // ---------- StaffOrderService.UpdateStatusAsync validation ----------

    [Fact]
    public async Task UpdateStatus_rejects_unknown_status_value()
    {
        await using var db = NewDb();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => Orders(db).UpdateStatusAsync(1, new StaffOrderStatusRequest { Status = "Shipped" }));

        Assert.Contains("ReadyForCollection, Rejected, or Collected", exception.Message);
    }

    [Fact]
    public async Task UpdateStatus_does_not_allow_moving_an_order_back_to_pending()
    {
        await using var db = NewDb();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Orders(db).UpdateStatusAsync(1, new StaffOrderStatusRequest { Status = "Pending" }));
    }

    [Fact]
    public async Task UpdateStatus_rejects_empty_status()
    {
        await using var db = NewDb();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Orders(db).UpdateStatusAsync(1, new StaffOrderStatusRequest { Status = "" }));
    }

    // ---------- StaffOrderService.ListActiveAsync ----------

    [Fact]
    public async Task ActiveQueue_is_empty_when_there_are_no_orders()
    {
        await using var db = NewDb();

        Assert.Empty(await Orders(db).ListActiveAsync());
    }

    [Fact]
    public async Task ActiveQueue_excludes_rejected_orders_and_lists_newest_first()
    {
        await using var db = NewDb();
        db.Orders.AddRange(
            new Order { Id = 5, CustomerId = Guid.NewGuid(), Status = OrderStatus.Pending },
            new Order { Id = 6, CustomerId = Guid.NewGuid(), Status = OrderStatus.Rejected, RejectionReason = "Out of stock" },
            new Order { Id = 7, CustomerId = Guid.NewGuid(), Status = OrderStatus.ReadyForCollection });
        await db.SaveChangesAsync();

        var queue = await Orders(db).ListActiveAsync();

        Assert.Equal(new[] { 7, 5 }, queue.Select(order => order.Id));
        Assert.DoesNotContain(queue, order => order.Status == "Rejected");
    }

    [Fact]
    public async Task ActiveQueue_maps_order_number_total_and_lines()
    {
        await using var db = NewDb();
        db.Orders.Add(new Order
        {
            Id = 12, CustomerId = Guid.NewGuid(), Status = OrderStatus.Pending, TotalAmount = 4600m,
            Items = new List<OrderItem>
            {
                new() { Id = 2, MedicineId = 1, MedicineName = "Paracetamol", Quantity = 3, UnitPrice = 200m },
                new() { Id = 1, MedicineId = 2, MedicineName = "Gaviscon", Quantity = 2, UnitPrice = 2000m }
            }
        });
        await db.SaveChangesAsync();

        var order = Assert.Single(await Orders(db).ListActiveAsync());

        Assert.Equal("CN-000012", order.OrderNumber);
        Assert.Equal(4600m, order.TotalAmount);
        Assert.Equal("Pending", order.Status);
        Assert.Equal(new[] { "Gaviscon", "Paracetamol" }, order.Items.Select(line => line.MedicineName));
        Assert.Equal(4000m, order.Items[0].LineTotal);
    }

    // ---------- StaffPrescriptionService validation & queue ----------

    [Fact]
    public async Task Decide_rejects_a_decision_that_is_neither_verified_nor_rejected()
    {
        await using var db = NewDb();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => Prescriptions(db).DecideAsync(1, new PrescriptionDecisionRequest { Decision = "Approved" }));

        Assert.Equal("Choose Verified or Rejected.", exception.Message);
    }

    [Fact]
    public async Task Decide_requires_a_reason_when_rejecting()
    {
        await using var db = NewDb();

        var missing = await Assert.ThrowsAsync<ArgumentException>(
            () => Prescriptions(db).DecideAsync(1, new PrescriptionDecisionRequest { Decision = "Rejected", Reason = "   " }));
        var tooLong = await Assert.ThrowsAsync<ArgumentException>(
            () => Prescriptions(db).DecideAsync(1, new PrescriptionDecisionRequest { Decision = "Rejected", Reason = new string('x', 1001) }));

        Assert.Contains("rejection reason", missing.Message);
        Assert.Contains("1000", tooLong.Message);
    }

    [Fact]
    public async Task PendingQueue_lists_only_unverified_prescriptions_oldest_first()
    {
        await using var db = NewDb();
        db.Medicines.Add(new Medicine { Id = 1, CategoryId = 1, Name = "Amoxicillin", Price = 500m, PrescriptionRequired = true });
        var patient = Guid.NewGuid();
        db.Prescriptions.AddRange(
            new Prescription { Id = 1, UploadedBy = patient, MedicineId = 1, Quantity = 1, OriginalFileName = "b.pdf", UploadedAt = new DateTime(2026, 9, 2) },
            new Prescription { Id = 2, UploadedBy = patient, MedicineId = 1, Quantity = 1, OriginalFileName = "a.pdf", UploadedAt = new DateTime(2026, 9, 1) },
            new Prescription { Id = 3, UploadedBy = patient, MedicineId = 1, Quantity = 1, OriginalFileName = "done.pdf", Status = "Verified", IsVerified = true },
            new Prescription { Id = 4, UploadedBy = patient, MedicineId = 1, Quantity = 1, OriginalFileName = "no.pdf", Status = "Rejected", DecisionReason = "Unreadable" });
        await db.SaveChangesAsync();

        var queue = await Prescriptions(db).ListPendingAsync();

        Assert.Equal(new[] { "a.pdf", "b.pdf" }, queue.Select(item => item.OriginalFileName));
        Assert.All(queue, item => Assert.Equal("PendingVerification", item.Status));
        Assert.All(queue, item => Assert.Equal("Amoxicillin", item.MedicineName));
    }

    // ---------- KafkaNotificationHandler: patient notifications ----------

    [Fact]
    public async Task OrderReadyForCollection_event_notifies_the_order_owner()
    {
        await using var db = NewDb();
        var patient = Guid.NewGuid();
        db.Orders.Add(new Order { Id = 9, CustomerId = patient, Status = OrderStatus.ReadyForCollection });
        await db.SaveChangesAsync();
        var eventId = Guid.NewGuid();

        await new KafkaNotificationHandler(db).HandleAsync(EventTypes.OrderTopic,
            OrderEventJson(eventId, EventTypes.OrderReadyForCollection, 9));

        var notification = Assert.Single(await db.PatientNotifications.ToListAsync());
        Assert.Equal(patient, notification.PatientId);
        Assert.Equal(eventId, notification.EventId);
        Assert.Equal("Your order is ready for collection", notification.Title);
        Assert.Equal("Order", notification.RelatedEntityType);
        Assert.Equal(9, notification.RelatedEntityId);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task OrderPlaced_event_creates_a_staff_notification_but_no_patient_notification()
    {
        await using var db = NewDb();
        db.Orders.Add(new Order { Id = 3, CustomerId = Guid.NewGuid(), Status = OrderStatus.Pending });
        await db.SaveChangesAsync();

        await new KafkaNotificationHandler(db).HandleAsync(EventTypes.OrderTopic,
            OrderEventJson(Guid.NewGuid(), EventTypes.OrderPlaced, 3));

        Assert.Single(await db.StaffNotifications.ToListAsync());
        Assert.Empty(await db.PatientNotifications.ToListAsync());
    }

    [Fact]
    public async Task Redelivered_order_status_event_does_not_duplicate_notifications()
    {
        await using var db = NewDb();
        db.Orders.Add(new Order { Id = 4, CustomerId = Guid.NewGuid(), Status = OrderStatus.Rejected, RejectionReason = "Expired stock" });
        await db.SaveChangesAsync();
        var json = OrderEventJson(Guid.NewGuid(), EventTypes.OrderRejected, 4);
        var handler = new KafkaNotificationHandler(db);

        await handler.HandleAsync(EventTypes.OrderTopic, json);
        await handler.HandleAsync(EventTypes.OrderTopic, json);

        Assert.Single(await db.StaffNotifications.ToListAsync());
        var notification = Assert.Single(await db.PatientNotifications.ToListAsync());
        Assert.Equal("Your order was rejected", notification.Title);
    }
}
