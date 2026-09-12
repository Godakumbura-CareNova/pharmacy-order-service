namespace PharmacyOrder.Api.Models;

public class Cart
{
    public int Id { get; set; }

    public Guid CustomerId { get; set; }

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}
