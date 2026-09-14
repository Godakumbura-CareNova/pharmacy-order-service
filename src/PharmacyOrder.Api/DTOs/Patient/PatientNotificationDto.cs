namespace PharmacyOrder.Api.DTOs.Patient;

public sealed class PatientNotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RelatedEntityType { get; set; } = string.Empty;
    public int RelatedEntityId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
}
