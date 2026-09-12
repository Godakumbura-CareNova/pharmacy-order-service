namespace PharmacyOrder.Api.DTOs.Medicine;

public class MedicineResponseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string? ImagePath { get; init; }
    public bool PrescriptionRequired { get; init; }
    public bool IsActive { get; init; }
}
