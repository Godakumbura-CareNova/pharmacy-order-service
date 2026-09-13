using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Services;

public sealed class StaffPrescriptionService(AppDbContext db)
{
    public async Task<IReadOnlyList<StaffPrescriptionDto>> ListPendingAsync(CancellationToken ct = default) =>
        await db.Prescriptions.AsNoTracking().Include(item => item.Medicine)
            .Where(item => item.Status == "PendingVerification")
            .OrderBy(item => item.UploadedAt).Select(item => new StaffPrescriptionDto
            {
                Id = item.Id, OriginalFileName = item.OriginalFileName, MedicineId = item.MedicineId,
                MedicineName = item.Medicine!.Name, Quantity = item.Quantity, Status = item.Status,
                UploadedAt = item.UploadedAt, DecisionReason = item.DecisionReason, ReviewedAtUtc = item.ReviewedAtUtc
            }).ToListAsync(ct);

    public async Task<StaffPrescriptionDto> DecideAsync(int id, PrescriptionDecisionRequest request, CancellationToken ct = default)
    {
        var decision = request.Decision?.Trim();
        var verified = string.Equals(decision, "Verified", StringComparison.Ordinal);
        var rejected = string.Equals(decision, "Rejected", StringComparison.Ordinal);
        if (!verified && !rejected) throw new ArgumentException("Choose Verified or Rejected.");
        if (rejected && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000))
            throw new ArgumentException("Provide a rejection reason of up to 1000 characters.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var prescription = await db.Prescriptions.Include(item => item.Medicine)
                .SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new KeyNotFoundException("Prescription was not found.");
            if (prescription.Status != "PendingVerification")
                throw new InvalidOperationException("This prescription has already been decided.");

            prescription.Status = verified ? "Verified" : "Rejected";
            prescription.IsVerified = verified;
            prescription.DecisionReason = rejected ? request.Reason!.Trim() : null;
            prescription.ReviewedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var eventId = Guid.NewGuid();
            var occurredAt = prescription.ReviewedAtUtc.Value;
            db.EventOutboxEntries.Add(new EventOutbox
            {
                EventId = eventId, Topic = EventTypes.OrderTopic, EventType = EventTypes.PrescriptionVerified,
                EventKey = prescription.Id.ToString(), OccurredAtUtc = occurredAt,
                PayloadJson = JsonSerializer.Serialize(new OrderEvent(eventId, EventTypes.PrescriptionVerified,
                    occurredAt, prescription.Id, "Prescription", JsonSerializer.SerializeToElement(new
                    {
                        Status = prescription.Status, prescription.MedicineId
                    })))
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToDto(prescription);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static StaffPrescriptionDto ToDto(Prescription prescription) => new()
    {
        Id = prescription.Id, OriginalFileName = prescription.OriginalFileName, MedicineId = prescription.MedicineId,
        MedicineName = prescription.Medicine!.Name, Quantity = prescription.Quantity, Status = prescription.Status,
        UploadedAt = prescription.UploadedAt, DecisionReason = prescription.DecisionReason, ReviewedAtUtc = prescription.ReviewedAtUtc
    };
}
