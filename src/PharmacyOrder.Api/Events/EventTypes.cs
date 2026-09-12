namespace PharmacyOrder.Api.Events;

public static class EventTypes
{
    public const string MedicineTopic = "carenova.medicine.events.v1";
    public const string InventoryTopic = "carenova.inventory.events.v1";

    public const string MedicineCreated = "MedicineCreated";
    public const string MedicineUpdated = "MedicineUpdated";
    public const string MedicineDeactivated = "MedicineDeactivated";
    public const string CategoryCreated = "CategoryCreated";
    public const string CategoryUpdated = "CategoryUpdated";
    public const string CategoryDeleted = "CategoryDeleted";
    public const string InventoryUpdated = "InventoryUpdated";
    public const string LowStockDetected = "LowStockDetected";
    public const string MedicineOutOfStock = "MedicineOutOfStock";
}
