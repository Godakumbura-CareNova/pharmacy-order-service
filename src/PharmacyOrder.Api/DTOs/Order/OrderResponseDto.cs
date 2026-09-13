namespace PharmacyOrder.Api.DTOs.Order;
public record OrderLineDto(string MedicineName, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}
public record OrderResponseDto(int Id, string OrderNumber, DateTime OrderDate, string Status,
    decimal TotalAmount, string? RejectionReason, IReadOnlyList<OrderLineDto> Items);
public record CheckoutReviewLineDto(int CartItemId, string MedicineName, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}
public record CheckoutReviewDto(IReadOnlyList<CheckoutReviewLineDto> Items)
{
    public decimal TotalAmount => Items.Sum(item => item.LineTotal);
}
