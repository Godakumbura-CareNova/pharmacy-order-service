using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Services;

public sealed class StaffOrderService(AppDbContext db, KafkaSettings settings)
{
    public async Task<IReadOnlyList<StaffOrderDto>> ListActiveAsync(CancellationToken ct = default)
    {
        var orders = await db.Orders.AsNoTracking().Include(order => order.Items)
            .Where(order => order.Status == OrderStatus.Pending || order.Status == OrderStatus.ReadyForCollection)
            .OrderByDescending(order => order.Id).ToListAsync(ct);
        return orders.Select(ToDto).ToList();
    }

    public async Task<OrderResponseDto> UpdateStatusAsync(int id, StaffOrderStatusRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrderStatus>(request.Status, true, out var status) || status == OrderStatus.Pending)
            throw new ArgumentException("Choose ReadyForCollection, Rejected, or Collected.");
        return await new OrderService(db, settings).TransitionAsync(id, status, request.Reason, ct);
    }

    private static StaffOrderDto ToDto(Order order) => new(order.Id, $"CN-{order.Id:D6}", order.OrderDate,
        order.Status.ToString(), order.TotalAmount, order.RejectionReason,
        order.Items.OrderBy(item => item.Id).Select(item => new OrderLineDto(item.MedicineName, item.Quantity, item.UnitPrice)).ToList());
}
