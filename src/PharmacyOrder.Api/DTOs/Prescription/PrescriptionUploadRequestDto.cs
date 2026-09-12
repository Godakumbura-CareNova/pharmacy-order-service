using Microsoft.AspNetCore.Http;

namespace PharmacyOrder.Api.DTOs.Prescription;

public class PrescriptionUploadRequestDto
{
    public int MedicineId { get; set; }

    public int Quantity { get; set; }

    public IFormFile? File { get; set; }
}
