namespace PharmacyOrder.Api.DTOs.Category;

public class CategoryResponseDto
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int MedicineCount { get; init; }
}
