using System.Text.Json;

namespace PharmacyOrder.Api.Events;

public sealed record MedicineEvent(
    Guid EventId,
    string EventType,
    DateTime OccurredAtUtc,
    int AggregateId,
    string AggregateType,
    JsonElement Data);
