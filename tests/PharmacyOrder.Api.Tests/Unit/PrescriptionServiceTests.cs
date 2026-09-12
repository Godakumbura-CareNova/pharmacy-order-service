using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Prescription;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class PrescriptionServiceTests
{
    [Fact]
    public async Task UploadAsync_CreatesPendingRecordForPrescriptionMedicine()
    {
        await using var db = CreateDbContext();
        var medicine = new Medicine { Name = "Amoxicillin", Description = "Antibiotic", Price = 300m, PrescriptionRequired = true, IsActive = true, StockQuantity = 10 };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        var service = new PrescriptionService(db, new FakeFileStorage());

        var patientId = Guid.NewGuid();
        var result = await service.UploadAsync(patientId, new PrescriptionUploadRequestDto
        {
            MedicineId = medicine.Id,
            Quantity = 2,
            File = CreateFile("doctor-note.pdf", "application/pdf")
        }, CancellationToken.None);

        Assert.Equal("PendingVerification", result.Status);
        Assert.Equal(2, result.Quantity);
        Assert.Equal("Amoxicillin", result.MedicineName);
        Assert.Equal(patientId, (await db.Prescriptions.SingleAsync()).UploadedBy);
    }

    [Fact]
    public async Task GetOwnedFileAsync_RejectsAnotherPatientsPrescription()
    {
        await using var db = CreateDbContext();
        var prescription = new Prescription { UploadedBy = Guid.NewGuid(), FilePath = "prescriptions/private.pdf" };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        var service = new PrescriptionService(db, new FakeFileStorage());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOwnedFileAsync(Guid.NewGuid(), prescription.Id));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }

    private static IFormFile CreateFile(string name, string contentType)
    {
        var content = new MemoryStream("prescription"u8.ToArray());
        return new FormFile(content, 0, content.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private sealed class FakeFileStorage : IFileStorageService
    {
        public string GetPrescriptionFilePath(string storedPath) => storedPath;
        public Task<string> SaveMedicineImageAsync(IFormFile file, CancellationToken cancellationToken) => Task.FromResult("/uploads/medicines/image.png");
        public Task<string> SavePrescriptionAsync(IFormFile file, CancellationToken cancellationToken) => Task.FromResult("prescriptions/private.pdf");
    }
}
