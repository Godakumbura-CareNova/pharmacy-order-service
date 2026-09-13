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
        await db.SaveChangesAsync(ct);
    }
}
