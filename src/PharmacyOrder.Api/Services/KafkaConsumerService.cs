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
        consumer.Subscribe(new[] { EventTypes.MedicineTopic, EventTypes.InventoryTopic });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var notification = result.Topic == EventTypes.InventoryTopic
                    ? JsonSerializer.Deserialize<InventoryEvent>(result.Message.Value) is { } inventoryEvent
                        ? (inventoryEvent.EventId, inventoryEvent.EventType, inventoryEvent.EntityType, inventoryEvent.MedicineId, inventoryEvent.OccurredAtUtc)
                        : ((Guid, string, string, int, DateTime)?)null
                    : JsonSerializer.Deserialize<MedicineEvent>(result.Message.Value) is { } medicineEvent
                        ? (medicineEvent.EventId, medicineEvent.EventType, medicineEvent.AggregateType, medicineEvent.AggregateId, medicineEvent.OccurredAtUtc)
                        : ((Guid, string, string, int, DateTime)?)null;

                if (notification is null)
                {
                    _logger.LogWarning("Ignoring unreadable Kafka event at {TopicPartitionOffset}.", result.TopicPartitionOffset);
                    consumer.Commit(result);
                    continue;
                }

                await CreateNotificationAsync(notification.Value.Item1, notification.Value.Item2, notification.Value.Item3, notification.Value.Item4, notification.Value.Item5, stoppingToken);
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

    private async Task CreateNotificationAsync(Guid eventId, string eventType, string entityType, int entityId, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await dbContext.StaffNotifications
            .AnyAsync(notification => notification.EventId == eventId, cancellationToken);

        if (exists)
        {
            return;
        }

        dbContext.StaffNotifications.Add(new StaffNotification
        {
            EventId = eventId,
            NotificationType = eventType,
            Title = eventType,
            Message = $"{entityType} #{entityId} changed.",
            RelatedEntityType = entityType,
            RelatedEntityId = entityId,
            CreatedAtUtc = occurredAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
