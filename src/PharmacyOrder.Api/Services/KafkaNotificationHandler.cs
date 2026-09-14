using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Services;

public sealed class KafkaNotificationHandler(AppDbContext db)
{
    public async Task HandleAsync(string topic, string json, CancellationToken ct = default)
    {
        var notification = topic switch
        {
            EventTypes.InventoryTopic when JsonSerializer.Deserialize<InventoryEvent>(json) is { } e
                => (e.EventId, e.EventType, e.EntityType, e.MedicineId, e.OccurredAtUtc),
            EventTypes.MedicineTopic when JsonSerializer.Deserialize<MedicineEvent>(json) is { } e
                => (e.EventId, e.EventType, e.AggregateType, e.AggregateId, e.OccurredAtUtc),
            EventTypes.OrderTopic when JsonSerializer.Deserialize<OrderEvent>(json) is { } e
                => (e.EventId, e.EventType, e.AggregateType, e.AggregateId, e.OccurredAtUtc),
            _ => throw new JsonException("Unsupported Kafka event.")
        };
        var (eventId, eventType, entityType, entityId, occurredAt) = notification;
        if (await db.StaffNotifications.AnyAsync(n => n.EventId == eventId, ct)) return;
        db.StaffNotifications.Add(new StaffNotification
        {
            EventId = eventId, NotificationType = eventType,
            Title = eventType == EventTypes.OrderPlaced ? "New order received" : eventType,
            Message = eventType == EventTypes.OrderPlaced
                ? $"Order #{entityId} is pending pharmacy review."
                : $"{entityType} #{entityId} changed.",
            RelatedEntityType = entityType, RelatedEntityId = entityId, CreatedAtUtc = occurredAt
        });
        if (topic == EventTypes.OrderTopic)
            await AddPatientNotificationAsync(eventId, eventType, entityType, entityId, occurredAt, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task AddPatientNotificationAsync(Guid eventId, string eventType, string entityType, int entityId, DateTime occurredAt, CancellationToken ct)
    {
        if (eventType == EventTypes.OrderPlaced || await db.PatientNotifications.AnyAsync(n => n.EventId == eventId, ct)) return;
        Guid patientId;
        if (entityType == "Order")
            patientId = (await db.Orders.AsNoTracking().Where(order => order.Id == entityId).Select(order => (Guid?)order.CustomerId).SingleOrDefaultAsync(ct))
                ?? throw new JsonException("Order notification owner was not found.");
        else if (entityType == "Prescription")
            patientId = (await db.Prescriptions.AsNoTracking().Where(item => item.Id == entityId).Select(item => (Guid?)item.UploadedBy).SingleOrDefaultAsync(ct))
                ?? throw new JsonException("Prescription notification owner was not found.");
        else return;

        var (title, message) = eventType switch
        {
            EventTypes.PrescriptionVerified => ("Your prescription was reviewed", $"Prescription #{entityId} was reviewed by the pharmacy."),
            EventTypes.OrderReadyForCollection => ("Your order is ready for collection", $"Order #{entityId} is ready for collection."),
            EventTypes.OrderRejected => ("Your order was rejected", $"Order #{entityId} was rejected by the pharmacy."),
            EventTypes.OrderCollected => ("Your order was collected", $"Order #{entityId} was marked as collected."),
            _ => ($"Your {entityType.ToLowerInvariant()} was updated", $"{entityType} #{entityId} was updated.")
        };
        db.PatientNotifications.Add(new PatientNotification { EventId = eventId, PatientId = patientId, Title = title,
            Message = message, RelatedEntityType = entityType, RelatedEntityId = entityId, CreatedAtUtc = occurredAt });
    }
}
