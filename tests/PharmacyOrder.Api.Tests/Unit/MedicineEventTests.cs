using System.Text.Json;
using PharmacyOrder.Api.Events;

namespace PharmacyOrder.Api.Tests.Unit;

public class MedicineEventTests
{
    [Fact]
    public void MedicineEvent_SerializesAndDeserializesWithoutLosingIdentity()
    {
        var original = new MedicineEvent(
            Guid.NewGuid(),
            EventTypes.MedicineCreated,
            DateTime.UtcNow,
            25,
            "Medicine",
            JsonSerializer.SerializeToElement(new { Name = "Paracetamol" }));

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<MedicineEvent>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.EventId, restored.EventId);
        Assert.Equal(original.EventType, restored.EventType);
        Assert.Equal(original.AggregateId, restored.AggregateId);
    }
}
