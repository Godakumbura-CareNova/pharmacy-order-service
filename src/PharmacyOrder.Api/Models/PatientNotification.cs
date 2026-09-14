namespace PharmacyOrder.Api.Models;

public class PatientNotification
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public Guid PatientId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RelatedEntityType { get; set; } = string.Empty;
    public int RelatedEntityId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
