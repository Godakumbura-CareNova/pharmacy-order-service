using Confluent.Kafka;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class KafkaEventProducer : IKafkaEventProducer
{
    private readonly KafkaSettings _settings;
    private readonly ILogger<KafkaEventProducer> _logger;

    public KafkaEventProducer(IOptions<KafkaSettings> settings, ILogger<KafkaEventProducer> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task PublishAsync(EventOutbox outboxEntry, CancellationToken cancellationToken)
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            // Fail fast when the broker is unreachable instead of blocking the outbox loop
            // for librdkafka's default 5-minute message timeout.
            MessageTimeoutMs = 10_000
        };

        using var producer = new ProducerBuilder<string, string>(producerConfig)
            .SetLogHandler((_, message) => _logger.LogDebug("Kafka producer: {Message}", message.Message))
            .SetErrorHandler((_, error) => _logger.LogWarning("Kafka producer error {Code}: {Reason}", error.Code, error.Reason))
            .Build();
        await producer.ProduceAsync(
            outboxEntry.Topic,
            new Message<string, string>
            {
                Key = outboxEntry.EventKey,
                Value = outboxEntry.PayloadJson
            },
            cancellationToken);
    }
}
