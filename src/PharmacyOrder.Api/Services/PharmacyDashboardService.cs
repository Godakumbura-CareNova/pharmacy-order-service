using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Dashboard;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Services;

public sealed class PharmacyDashboardService(AppDbContext db, IOptions<KafkaSettings> settings)
{
    public async Task<PatientOrderDashboard> GetPatientOrdersAsync(Guid patientId, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == patientId);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id).Take(5).ToListAsync(ct);
        return new(count, items.Select(ToOrder).ToArray(), DateTime.UtcNow);
    }

    public async Task<StaffOperationsDashboard> GetStaffOperationsAsync(CancellationToken ct)
    {
        var pending = await Orders(OrderStatus.Pending, false, ct);
        var ready = await Orders(OrderStatus.ReadyForCollection, true, ct);
        var prescriptions = db.Prescriptions.AsNoTracking().Where(p => p.Status == "PendingVerification");
        var prescriptionCount = await prescriptions.CountAsync(ct);
        var prescriptionRows = await prescriptions.OrderBy(p => p.UploadedAt).ThenBy(p => p.Id).Take(5)
            .Select(p => new { p.Id, MedicineName = p.Medicine!.Name, p.Quantity, p.UploadedAt, p.Status }).ToListAsync(ct);
        var threshold = settings.Value.LowStockThreshold;
        var stock = db.Medicines.AsNoTracking().Where(m => m.IsActive && m.StockQuantity <= threshold);
        var stockCount = await stock.CountAsync(ct);
        var stockRows = await stock.OrderBy(m => m.StockQuantity).ThenBy(m => m.Name).ThenBy(m => m.Id).Take(5)
            .Select(m => new DashboardStock(m.Id, m.Name, m.StockQuantity, threshold)).ToListAsync(ct);
        return new(pending, new(prescriptionCount, prescriptionRows.Select(p => new DashboardPrescription(
            p.Id, p.MedicineName, p.Quantity, Utc(p.UploadedAt), p.Status)).ToArray()),
            new(stockCount, stockRows), ready, DateTime.UtcNow);
    }

    private async Task<DashboardQueue<DashboardOrder>> Orders(OrderStatus status, bool oldestFirst, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.Status == status);
        var count = await query.CountAsync(ct);
        var sorted = oldestFirst ? query.OrderBy(o => o.OrderDate).ThenBy(o => o.Id)
            : query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id);
        return new(count, (await sorted.Take(5).ToListAsync(ct)).Select(ToOrder).ToArray());
    }

    private static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);
    private static DashboardOrder ToOrder(Order o) => new(o.Id, $"CN-{o.Id:D6}", Utc(o.OrderDate), o.Status.ToString(), o.TotalAmount);
}
