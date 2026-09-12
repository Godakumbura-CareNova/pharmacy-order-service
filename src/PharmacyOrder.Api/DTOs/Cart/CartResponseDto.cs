namespace PharmacyOrder.Api.DTOs.Cart;

public class CartResponseDto
{
    public IReadOnlyList<CartItemResponseDto> ReadyItems { get; init; } = [];

    public IReadOnlyList<CartItemResponseDto> PendingItems { get; init; } = [];

    public IReadOnlyList<CartItemResponseDto> AttentionItems { get; init; } = [];

    public decimal ReadySubtotal { get; init; }

    public decimal PendingSubtotal { get; init; }

    public decimal AttentionSubtotal { get; init; }

    public decimal CartTotal { get; init; }
}
