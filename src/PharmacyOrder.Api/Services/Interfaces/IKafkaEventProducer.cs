using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface IKafkaEventProducer
{
    Task PublishAsync(EventOutbox outboxEntry, CancellationToken cancellationToken);
}
