using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.DTOs.Prescription;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.UnitMocking.Tests;

// GCN-29 Prescription Upload
public class GCN29_PrescriptionUploadTests
{
    private static readonly Guid PatientId = Guid.NewGuid();

    private readonly AppDbContext _db = TestDoubles.FakeDb();
    private readonly Mock<IFileStorageService> _files = new();
    private readonly Mock<ICartService> _cart = new();
    private readonly PrescriptionService _sut;

    public GCN29_PrescriptionUploadTests()
    {
        _db.Medicines.Add(new Medicine { Id = 3, Name = "Amoxicillin", PrescriptionRequired = true, StockQuantity = 5, Price = 200m });
        _db.SaveChanges();
        _files.Setup(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>())).ReturnsAsync("prescriptions/rx.pdf");
        _cart.Setup(c => c.AddPrescriptionItemAsync(PatientId, It.IsAny<Prescription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartResponseDto());
        _sut = new PrescriptionService(_db, _files.Object, _cart.Object);
    }

    private static PrescriptionUploadRequestDto Request(int quantity = 2, IFormFile? file = null) =>
        new() { MedicineId = 3, Quantity = quantity, File = file ?? TestDoubles.File("rx.pdf", 1024) };

    // valid upload is stored, saved as pending and added to cart
    [Fact]
    public async Task UploadAsync_ValidFile_SavesPendingPrescription_AndAddsToCart()
    {
        var result = await _sut.UploadAsync(PatientId, Request(), CancellationToken.None);

        Assert.Equal(("PendingVerification", "Amoxicillin", "rx.pdf"), (result.Status, result.MedicineName, result.OriginalFileName));
        _files.Verify(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Once);
        _cart.Verify(c => c.AddPrescriptionItemAsync(PatientId, It.Is<Prescription>(p => p.Quantity == 2 && p.FilePath == "prescriptions/rx.pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // file is stored before the cart item is added
    [Fact]
    public async Task UploadAsync_StoresFileBeforeAddingToCart()
    {
        var calls = new List<string>();
        _files.Setup(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("save file")).ReturnsAsync("prescriptions/rx.pdf");
        _cart.Setup(c => c.AddPrescriptionItemAsync(PatientId, It.IsAny<Prescription>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("add to cart")).ReturnsAsync(new CartResponseDto());

        await _sut.UploadAsync(PatientId, Request(), CancellationToken.None);

        Assert.Equal(new[] { "save file", "add to cart" }, calls);
    }

    // missing file is rejected and nothing is stored
    [Fact]
    public async Task UploadAsync_NoFile_IsRejected_AndStorageNeverCalled()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UploadAsync(PatientId, new PrescriptionUploadRequestDto { MedicineId = 3, Quantity = 1 }, CancellationToken.None));

        _files.Verify(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Never);
        _cart.Verify(c => c.AddPrescriptionItemAsync(It.IsAny<Guid>(), It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // quantity must be between 1 and stock
    [Theory]
    [InlineData(6)]
    public async Task UploadAsync_InvalidQuantity_IsRejected(int quantity)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UploadAsync(PatientId, Request(quantity), CancellationToken.None));

        _files.Verify(f => f.SavePrescriptionAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_db.Prescriptions);
    }

    // only PDF, JPG, JPEG and PNG up to 10 MB are accepted
    [Theory]
    [InlineData("rx.exe", 1024)]
    public async Task SavePrescriptionAsync_InvalidFile_IsRejected(string fileName, long size)
    {
        var storage = new FileStorageService(Options.Create(new FileStorageSettings()),
            Options.Create(new PrescriptionStorageSettings { AllowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" } }));

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SavePrescriptionAsync(TestDoubles.File(fileName, size), CancellationToken.None));
    }
}
