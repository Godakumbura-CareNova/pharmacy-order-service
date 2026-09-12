namespace PharmacyOrder.Api.DTOs.Inventory;

public class MedicineInventoryResponseDto
{
    public int MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
    public bool IsLowStock { get; set; }
}
