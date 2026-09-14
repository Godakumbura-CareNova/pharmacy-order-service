namespace PharmacyOrder.Api.Config;

public class KafkaSettings
{
    /// <summary>
    /// When false, the outbox publisher and consumer are skipped so the API can run
    /// without a broker (events stay queued in EventOutboxEntries until re-enabled).
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ConsumerGroupId { get; set; } = "carenova-pharmacy-notifications";

    public int LowStockThreshold { get; set; } = 10;

    public int PublishRetryLimit { get; set; } = 3;
}
