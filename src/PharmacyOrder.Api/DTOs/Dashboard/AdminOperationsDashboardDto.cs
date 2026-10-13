namespace PharmacyOrder.Api.DTOs.Dashboard;

public sealed record AdminOperationsDashboardDto(int TotalOrders, IReadOnlyDictionary<string, int> OrderStatusCounts,
    IReadOnlyList<DashboardOrder> RecentOrders, DashboardQueue<DashboardStock> LowStock, DateTime GeneratedAtUtc);
