namespace PharmacyOrder.Api.Models;

public class Prescription
{
    public int Id { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public int UploadedBy { get; set; }

    public bool IsVerified { get; set; }

    public DateTime UploadedAt { get; set; }
}