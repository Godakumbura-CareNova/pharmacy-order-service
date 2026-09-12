namespace PharmacyOrder.Api.DTOs.Medicine;

public class MedicineRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public bool PrescriptionRequired { get; set; }
}
