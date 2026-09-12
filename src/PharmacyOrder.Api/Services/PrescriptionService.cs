using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Prescription;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class PrescriptionService : IPrescriptionService
{
    private readonly AppDbContext _db;
    private readonly IFileStorageService _files;

    public PrescriptionService(AppDbContext db, IFileStorageService files)
    {
        _db = db;
        _files = files;
    }

    public async Task<PrescriptionResponseDto> UploadAsync(int patientId, PrescriptionUploadRequestDto request, CancellationToken cancellationToken)
    {
        if (request.File is null) throw new ArgumentException("A prescription file is required.");

        var medicine = await _db.Medicines.SingleOrDefaultAsync(item =>
            item.Id == request.MedicineId && item.IsActive && item.PrescriptionRequired && item.StockQuantity > 0,
            cancellationToken) ?? throw new KeyNotFoundException("Prescription medicine is unavailable.");

        if (request.Quantity < 1 || request.Quantity > medicine.StockQuantity)
            throw new ArgumentException($"Quantity must be between 1 and {medicine.StockQuantity}.");

        var storedPath = await _files.SavePrescriptionAsync(request.File, cancellationToken);
        var prescription = new Prescription
        {
            FilePath = storedPath,
            OriginalFileName = Path.GetFileName(request.File.FileName),
            UploadedBy = patientId,
            MedicineId = medicine.Id,
            Quantity = request.Quantity,
            Status = "PendingVerification",
            UploadedAt = DateTime.UtcNow
        };

        _db.Prescriptions.Add(prescription);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(prescription, medicine.Name);
    }

    public async Task<IReadOnlyList<PrescriptionResponseDto>> GetForPatientAsync(int patientId)
    {
        return await _db.Prescriptions
            .Where(prescription => prescription.UploadedBy == patientId)
            .Include(prescription => prescription.Medicine)
            .OrderByDescending(prescription => prescription.UploadedAt)
            .Select(prescription => new PrescriptionResponseDto
            {
                Id = prescription.Id,
                OriginalFileName = prescription.OriginalFileName,
                MedicineId = prescription.MedicineId,
                MedicineName = prescription.Medicine!.Name,
                Quantity = prescription.Quantity,
                Status = prescription.Status,
                UploadedAt = prescription.UploadedAt
            })
            .ToListAsync();
    }

    public async Task<PrescriptionFileResult> GetOwnedFileAsync(int patientId, int prescriptionId)
    {
        var prescription = await _db.Prescriptions.SingleOrDefaultAsync(item => item.Id == prescriptionId && item.UploadedBy == patientId)
            ?? throw new KeyNotFoundException("Prescription was not found.");
        var physicalPath = _files.GetPrescriptionFilePath(prescription.FilePath);
        if (!File.Exists(physicalPath)) throw new KeyNotFoundException("Prescription file was not found.");
        return new PrescriptionFileResult { PhysicalPath = physicalPath, OriginalFileName = prescription.OriginalFileName };
    }

    private static PrescriptionResponseDto ToResponse(Prescription prescription, string medicineName) => new()
    {
        Id = prescription.Id,
        OriginalFileName = prescription.OriginalFileName,
        MedicineId = prescription.MedicineId,
        MedicineName = medicineName,
        Quantity = prescription.Quantity,
        Status = prescription.Status,
        UploadedAt = prescription.UploadedAt
    };
}
