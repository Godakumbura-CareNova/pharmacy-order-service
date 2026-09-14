namespace PharmacyOrder.Api.DTOs.Prescription;

public class PrescriptionResponseDto
{
    public int Id { get; init; }

    public string OriginalFileName { get; init; } = string.Empty;

    public int MedicineId { get; init; }

    public string MedicineName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTime UploadedAt { get; init; }

    public string? DecisionReason { get; init; }

    public DateTime? ReviewedAtUtc { get; init; }
}
