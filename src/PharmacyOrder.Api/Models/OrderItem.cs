namespace PharmacyOrder.Api.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int MedicineId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public string MedicineName { get; set; } = string.Empty;

    public int? PrescriptionId { get; set; }

    public Prescription? Prescription { get; set; }

    public ICollection<OrderStockAllocation> Allocations { get; set; } = new List<OrderStockAllocation>();

    public Order? Order { get; set; }

    public Medicine? Medicine { get; set; }
}
