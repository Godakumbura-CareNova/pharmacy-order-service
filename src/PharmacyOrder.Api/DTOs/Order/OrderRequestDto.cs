namespace PharmacyOrder.Api.DTOs.Order;
public class OrderRequestDto
{
    public Guid RequestId { get; set; }
    public List<CheckoutItemDto> Items { get; set; } = new();
}
public class CheckoutItemDto
{
    public int CartItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
