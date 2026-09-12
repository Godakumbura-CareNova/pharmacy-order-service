using System.ComponentModel.DataAnnotations;

namespace PharmacyOrder.Api.DTOs.Cart;

public class UpdateCartItemRequestDto
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}
