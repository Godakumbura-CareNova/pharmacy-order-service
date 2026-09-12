using System.Text.Json;

namespace PharmacyOrder.Api.Events;

public record InventoryEvent(
    Guid EventId,
    string EventType,
    DateTime OccurredAtUtc,
    int MedicineId,
    string EntityType,
    JsonElement Data);
