using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Prescription;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>GCN-29 unit tests 1–5 for PrescriptionService (file storage is mocked; see PrescriptionStorageTests for 6–7).</summary>
public class PrescriptionServiceQaTests
{
    private static readonly Guid Patient = Guid.NewGuid();

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task<Medicine> SeedAsync(AppDbContext db, bool prescription = true, int stock = 10, bool active = true)
    {
        var medicine = new Medicine
        {
            Name = prescription ? "Amoxicillin 500mg" : "Paracetamol",
            Description = "Test medicine",
            Category = new Category { Name = "Test" },
            Price = 100m,
            PrescriptionRequired = prescription,
            StockQuantity = stock,
            IsActive = active
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine;
    }

    private static IFormFile PdfFile(string name = "doctor-note.pdf")
    {
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 test prescription");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    private static (PrescriptionService service, Mock<IFileStorageService> files) CreateService(AppDbContext db)
    {
        var files = new Mock<IFileStorageService>();
        files.Setup(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync("prescriptions/stored-file.pdf");
        // dev (GCN-30+) links uploads to the patient's cart through ICartService; stub it so upload tests stay focused.
        var cart = new Mock<PharmacyOrder.Api.Services.Interfaces.ICartService>();
        cart.Setup(c => c.AddPrescriptionItemAsync(It.IsAny<Guid>(), It.IsAny<Prescription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PharmacyOrder.Api.DTOs.Cart.CartResponseDto());
        return (new PrescriptionService(db, files.Object, cart.Object), files);
    }

    // 1 — Scenario 2: a valid upload stores the medicine, quantity, owner and file together
    [Fact]
    public async Task UploadAsync_ValidPdf_StoresPrescriptionWithMedicineQuantityAndOwner()
    {
        await using var db = CreateDb();
        var medicine = await SeedAsync(db);
        var (service, files) = CreateService(db);

        var result = await service.UploadAsync(Patient, new PrescriptionUploadRequestDto { MedicineId = medicine.Id, Quantity = 2, File = PdfFile() }, CancellationToken.None);

        var stored = Assert.Single(db.Prescriptions);
        Assert.Equal(medicine.Id, stored.MedicineId);
        Assert.Equal(2, stored.Quantity);
        Assert.Equal(Patient, stored.UploadedBy);
        Assert.Equal("prescriptions/stored-file.pdf", stored.FilePath);
        Assert.Equal("doctor-note.pdf", stored.OriginalFileName);
        Assert.Equal("PendingVerification", stored.Status);
        Assert.Equal("Amoxicillin 500mg", result.MedicineName);
        Assert.Equal(2, result.Quantity);
        files.Verify(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // 2 — Scenario 3: a prescription medicine without a file is blocked with a warning
    [Fact]
    public async Task UploadAsync_WithoutFile_IsBlockedWithWarning()
    {
        await using var db = CreateDb();
        var medicine = await SeedAsync(db);
        var (service, files) = CreateService(db);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadAsync(Patient, new PrescriptionUploadRequestDto { MedicineId = medicine.Id, Quantity = 1, File = null }, CancellationToken.None));

        Assert.Equal("A prescription file is required.", exception.Message);
        Assert.Empty(db.Prescriptions);
        files.Verify(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 3 — prescription requirement check: only prescription-required, active, in-stock medicines accept an upload
    [Fact]
    public async Task UploadAsync_NonPrescriptionInactiveOrOutOfStockMedicine_IsRejected()
    {
        await using var db = CreateDb();
        var nonPrescription = await SeedAsync(db, prescription: false);
        var inactive = await SeedAsync(db, active: false);
        var outOfStock = await SeedAsync(db, stock: 0);
        var (service, _) = CreateService(db);

        foreach (var id in new[] { nonPrescription.Id, inactive.Id, outOfStock.Id, 999_999 })
        {
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.UploadAsync(Patient, new PrescriptionUploadRequestDto { MedicineId = id, Quantity = 1, File = PdfFile() }, CancellationToken.None));
            Assert.Equal("Prescription medicine is unavailable.", exception.Message);
        }
        Assert.Empty(db.Prescriptions);
    }

    // 4 — quantity carried from the cart selection must be within 1..stock
    [Fact]
    public async Task UploadAsync_QuantityOutsideStock_IsRejected()
    {
        await using var db = CreateDb();
        var medicine = await SeedAsync(db, stock: 5);
        var (service, _) = CreateService(db);

        foreach (var quantity in new[] { 0, -1, 6 })
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.UploadAsync(Patient, new PrescriptionUploadRequestDto { MedicineId = medicine.Id, Quantity = quantity, File = PdfFile() }, CancellationToken.None));
            Assert.Equal("Quantity must be between 1 and 5.", exception.Message);
        }
    }

    // 5 — a customer only sees their own prescriptions, newest first, and can only open their own file
    [Fact]
    public async Task GetForPatientAsync_ReturnsOnlyOwnPrescriptionsNewestFirst_AndFileAccessIsOwnerOnly()
    {
        await using var db = CreateDb();
        var medicine = await SeedAsync(db);
        var other = Guid.NewGuid();
        db.Prescriptions.AddRange(
            new Prescription { MedicineId = medicine.Id, UploadedBy = Patient, Quantity = 1, FilePath = "prescriptions/old.pdf", OriginalFileName = "old.pdf", UploadedAt = new DateTime(2026, 9, 1) },
            new Prescription { MedicineId = medicine.Id, UploadedBy = Patient, Quantity = 2, FilePath = "prescriptions/new.pdf", OriginalFileName = "new.pdf", UploadedAt = new DateTime(2026, 9, 10) },
            new Prescription { MedicineId = medicine.Id, UploadedBy = other, Quantity = 3, FilePath = "prescriptions/theirs.pdf", OriginalFileName = "theirs.pdf", UploadedAt = new DateTime(2026, 9, 12) });
        await db.SaveChangesAsync();
        var (service, files) = CreateService(db);
        files.Setup(f => f.GetPrescriptionFilePath(It.IsAny<string>())).Returns<string>(p => Path.Combine(Path.GetTempPath(), Path.GetFileName(p)));

        var mine = await service.GetForPatientAsync(Patient);
        var theirs = db.Prescriptions.Single(p => p.UploadedBy == other);

        Assert.Equal(new[] { "new.pdf", "old.pdf" }, mine.Select(p => p.OriginalFileName).ToArray());
        Assert.All(mine, p => Assert.Equal("Amoxicillin 500mg", p.MedicineName));
        var denied = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOwnedFileAsync(Patient, theirs.Id));
        Assert.Equal("Prescription was not found.", denied.Message);
    }
}
