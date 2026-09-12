namespace PharmacyOrder.Api.Models;

public class EventOutbox
{
    public int Id { get; set; }

    public Guid EventId { get; set; } = Guid.NewGuid();

    public string Topic { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string EventKey { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? PublishedAtUtc { get; set; }

    public int PublishAttempts { get; set; }

    public string? LastError { get; set; }
}
