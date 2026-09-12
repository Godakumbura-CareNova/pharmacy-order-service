namespace PharmacyOrder.Api.Config;

public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ConsumerGroupId { get; set; } = "carenova-pharmacy-notifications";

    public int LowStockThreshold { get; set; } = 10;

    public int PublishRetryLimit { get; set; } = 3;
}
