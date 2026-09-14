using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class PrescriptionCleanupService : IPrescriptionCleanupService
{
    private readonly AppDbContext _db;
    private readonly IFileStorageService _files;

    public PrescriptionCleanupService(AppDbContext db, IFileStorageService files)
    {
        _db = db;
        _files = files;
    }

    public async Task DeletePendingAsync(Guid patientId, int prescriptionId, CancellationToken cancellationToken)
    {
        var prescription = await _db.Prescriptions.SingleOrDefaultAsync(item =>
            item.Id == prescriptionId && item.UploadedBy == patientId, cancellationToken)
            ?? throw new KeyNotFoundException("Prescription was not found.");

        if (prescription.Status != "PendingVerification")
            throw new InvalidOperationException("Only prescriptions awaiting verification can be deleted.");

        var cartItems = await _db.CartItems.Where(item =>
            item.PrescriptionId == prescription.Id && item.Cart!.CustomerId == patientId).ToListAsync(cancellationToken);
        _db.CartItems.RemoveRange(cartItems);
        _db.Prescriptions.Remove(prescription);
        await _db.SaveChangesAsync(cancellationToken);
        await _files.DeletePrescriptionAsync(prescription.FilePath, cancellationToken);
    }
}
