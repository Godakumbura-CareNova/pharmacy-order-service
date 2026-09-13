namespace PharmacyOrder.Api.Models;

public class Order
{
    public int Id { get; set; }

    public Guid CustomerId { get; set; }

    public Guid RequestId { get; set; }

    public string RequestFingerprint { get; set; } = string.Empty;

    public string? RejectionReason { get; set; }

    public DateTime OrderDate { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public decimal TotalAmount { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
