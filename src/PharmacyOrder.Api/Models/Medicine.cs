namespace PharmacyOrder.Api.Models;

public class Medicine
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    public decimal Price { get; set; }

    public string? ImagePath { get; set; }

    public bool PrescriptionRequired { get; set; }

    public int StockQuantity { get; set; }

    public Category? Category { get; set; }
}