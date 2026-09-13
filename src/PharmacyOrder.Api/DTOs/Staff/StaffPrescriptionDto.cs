namespace PharmacyOrder.Api.DTOs.Staff;

public sealed class StaffPrescriptionDto
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

public sealed class PrescriptionDecisionRequest
{
    public string Decision { get; init; } = string.Empty;
    public string? Reason { get; init; }
}
