namespace PharmacyOrder.Api.Models;

public class Stock
{
    public int Id { get; set; }

    public int MedicineId { get; set; }

    public string BatchNumber { get; set; } = string.Empty;

    public decimal PurchasePrice { get; set; }

    public decimal SellingPrice { get; set; }

    public DateTime ManufacturingDate { get; set; }

    public DateTime ExpiryDate { get; set; }

    public int Quantity { get; set; }

    public Medicine? Medicine { get; set; }
}