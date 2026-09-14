using PharmacyOrder.Api.DTOs.Order;

namespace PharmacyOrder.Api.DTOs.Staff;

public sealed record StaffOrderDto(int Id, string OrderNumber, DateTime OrderDate, string Status,
    decimal TotalAmount, string? RejectionReason, IReadOnlyList<OrderLineDto> Items);

public sealed class StaffOrderStatusRequest
{
    public string Status { get; init; } = string.Empty;
    public string? Reason { get; init; }
}
