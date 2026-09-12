namespace PharmacyOrder.Api.DTOs.Catalogue;

public class CatalogueMedicineResponseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string? ImagePath { get; init; }
    public bool PrescriptionRequired { get; init; }
    public int StockQuantity { get; init; }
    public string AvailabilityStatus { get; init; } = string.Empty;
}
