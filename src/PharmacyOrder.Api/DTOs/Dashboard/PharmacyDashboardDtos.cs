namespace PharmacyOrder.Api.DTOs.Dashboard;

public record DashboardQueue<T>(int TotalCount, IReadOnlyList<T> Items);
public record DashboardOrder(int Id, string Reference, DateTime OrderDate, string Status, decimal TotalAmount);
public record DashboardPrescription(int Id, string MedicineName, int Quantity, DateTime UploadedAtUtc, string Status);
public record DashboardStock(int MedicineId, string MedicineName, int Quantity, int Threshold);
public record PatientOrderDashboard(int TotalCount, IReadOnlyList<DashboardOrder> RecentOrders, DateTime GeneratedAtUtc);
public record StaffOperationsDashboard(DashboardQueue<DashboardOrder> NewOrders,
    DashboardQueue<DashboardPrescription> PendingPrescriptions, DashboardQueue<DashboardStock> LowStock,
    DashboardQueue<DashboardOrder> ReadyForCollection, DateTime GeneratedAtUtc);
