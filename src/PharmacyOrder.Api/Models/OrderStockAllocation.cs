namespace PharmacyOrder.Api.Models;

public class OrderStockAllocation
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public OrderItem? OrderItem { get; set; }
    public int StockId { get; set; }
    public Stock? Stock { get; set; }
    public int Quantity { get; set; }
}
