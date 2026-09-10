namespace PharmacyOrder.Api.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int MedicineId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public Order? Order { get; set; }

    public Medicine? Medicine { get; set; }
}