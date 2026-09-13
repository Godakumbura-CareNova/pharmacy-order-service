using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class OutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherService> _logger;
    private readonly KafkaSettings _settings;

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Kafka is disabled (Kafka:Enabled=false); outbox events will stay queued until it is enabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingEventsAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unable to publish pending Kafka events.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task PublishPendingEventsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var producer = scope.ServiceProvider.GetRequiredService<IKafkaEventProducer>();
        var entries = await dbContext.EventOutboxEntries
            .Where(entry => entry.PublishedAtUtc == null && entry.PublishAttempts < _settings.PublishRetryLimit)
            .OrderBy(entry => entry.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            entry.PublishAttempts++;

            try
            {
                await producer.PublishAsync(entry, cancellationToken);
                entry.PublishedAtUtc = DateTime.UtcNow;
                entry.LastError = null;
            }
            catch (Exception exception)
            {
                entry.LastError = exception.Message;
                _logger.LogWarning(exception, "Kafka publish failed for outbox event {EventId}.", entry.EventId);
            }
        }

        if (entries.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
