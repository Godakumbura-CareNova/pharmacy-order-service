using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaSettings _settings;
    private readonly ILogger<KafkaConsumerService> _logger;

    public KafkaConsumerService(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings,
        ILogger<KafkaConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(EventTypes.MedicineTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var medicineEvent = JsonSerializer.Deserialize<MedicineEvent>(result.Message.Value);

                if (medicineEvent is null)
                {
                    _logger.LogWarning("Ignoring unreadable Kafka event at {TopicPartitionOffset}.", result.TopicPartitionOffset);
                    consumer.Commit(result);
                    continue;
                }

                await CreateNotificationAsync(medicineEvent, stoppingToken);
                consumer.Commit(result);
            }
            catch (ConsumeException exception)
            {
                _logger.LogError(exception, "Kafka consumer error.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Kafka event handling failed; the event will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task CreateNotificationAsync(MedicineEvent medicineEvent, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await dbContext.StaffNotifications
            .AnyAsync(notification => notification.EventId == medicineEvent.EventId, cancellationToken);

        if (exists)
        {
            return;
        }

        dbContext.StaffNotifications.Add(new StaffNotification
        {
            EventId = medicineEvent.EventId,
            NotificationType = medicineEvent.EventType,
            Title = medicineEvent.EventType,
            Message = $"{medicineEvent.AggregateType} #{medicineEvent.AggregateId} changed.",
            RelatedEntityType = medicineEvent.AggregateType,
            RelatedEntityId = medicineEvent.AggregateId,
            CreatedAtUtc = medicineEvent.OccurredAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
