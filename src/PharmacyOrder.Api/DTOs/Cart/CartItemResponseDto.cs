namespace PharmacyOrder.Api.DTOs.Cart;

public class CartItemResponseDto
{
    public int Id { get; init; }

    public int MedicineId { get; init; }

    public string MedicineName { get; init; } = string.Empty;

    public string? MedicineImagePath { get; init; }

    public decimal UnitPrice { get; init; }

    public int Quantity { get; init; }

    public decimal LineTotal { get; init; }

    public int? PrescriptionId { get; init; }

    public string? PrescriptionStatus { get; init; }
}
