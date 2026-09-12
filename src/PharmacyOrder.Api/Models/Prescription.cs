namespace PharmacyOrder.Api.Models;

public class Prescription
{
    public int Id { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public int UploadedBy { get; set; }

    public int MedicineId { get; set; }

    public Medicine? Medicine { get; set; }

    public int Quantity { get; set; }

    public string Status { get; set; } = "PendingVerification";

    public bool IsVerified { get; set; }

    public DateTime UploadedAt { get; set; }
}
