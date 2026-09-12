using System.ComponentModel.DataAnnotations;

namespace PharmacyOrder.Api.DTOs.Cart;

public class AddCartItemRequestDto
{
    [Range(1, int.MaxValue)]
    public int MedicineId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}
