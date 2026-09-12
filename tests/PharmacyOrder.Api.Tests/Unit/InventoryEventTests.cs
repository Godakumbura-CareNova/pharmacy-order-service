using System.Text.Json;
using PharmacyOrder.Api.Events;

namespace PharmacyOrder.Api.Tests.Unit;

public class InventoryEventTests
{
    [Fact]
    public void InventoryEvent_PreservesInventoryUpdatedTypeAndMedicineId()
    {
        var inventoryEvent = new InventoryEvent(
            Guid.NewGuid(),
            EventTypes.InventoryUpdated,
            DateTime.UtcNow,
            14,
            "Inventory",
            JsonSerializer.SerializeToElement(new { Quantity = 8 }));

        Assert.Equal(EventTypes.InventoryUpdated, inventoryEvent.EventType);
        Assert.Equal(14, inventoryEvent.MedicineId);
    }
}
