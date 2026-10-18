using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.UnitMocking.Tests;

// GCN-30 Cart Management
public class GCN30_CartManagementTests
{
    private static readonly Guid PatientId = Guid.NewGuid();

    private static AppDbContext DbWithMedicines()
    {
        var db = TestDoubles.FakeDb();
        db.Medicines.AddRange(
            new Medicine { Id = 1, Name = "Panadol", Price = 50m, StockQuantity = 10 },
            new Medicine { Id = 2, Name = "Amoxicillin", Price = 200m, StockQuantity = 10, PrescriptionRequired = true });
        db.SaveChanges();
        return db;
    }

    // controller uses the logged in patient id
    [Fact]
    public async Task AddItem_UsesLoggedInPatient_AndReturnsOk()
    {
        var request = new AddCartItemRequestDto { MedicineId = 1, Quantity = 2 };
        var service = new Mock<ICartService>();
        service.Setup(s => s.AddNonPrescriptionItemAsync(PatientId, request, It.IsAny<CancellationToken>())).ReturnsAsync(new CartResponseDto());

        var result = await new CartController(service.Object).WithUser(PatientId).AddItem(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        service.Verify(s => s.AddNonPrescriptionItemAsync(PatientId, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    // service errors are turned into the right status code
    [Theory]
    [InlineData(typeof(KeyNotFoundException), typeof(NotFoundObjectResult))]
    public async Task UpdateItem_ServiceError_ReturnsMatchingStatus(Type exceptionType, Type expectedResult)
    {
        var service = new Mock<ICartService>();
        service.Setup(s => s.UpdateItemAsync(PatientId, 4, It.IsAny<UpdateCartItemRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exceptionType, "error")!);

        var result = await new CartController(service.Object).WithUser(PatientId)
            .UpdateItem(4, new UpdateCartItemRequestDto { Quantity = 3 }, CancellationToken.None);

        Assert.IsType(expectedResult, result.Result);
    }

    // adding the same medicine again increases the quantity
    [Fact]
    public async Task AddNonPrescriptionItemAsync_SameMedicineTwice_MergesQuantity()
    {
        var cart = new CartService(DbWithMedicines(), Mock.Of<IPrescriptionCleanupService>());

        await cart.AddNonPrescriptionItemAsync(PatientId, new AddCartItemRequestDto { MedicineId = 1, Quantity = 2 }, CancellationToken.None);
        var result = await cart.AddNonPrescriptionItemAsync(PatientId, new AddCartItemRequestDto { MedicineId = 1, Quantity = 3 }, CancellationToken.None);

        var line = Assert.Single(result.ReadyItems);
        Assert.Equal((5, 250m), (line.Quantity, result.CartTotal));
    }

    // prescription medicine must go through prescription upload
    [Fact]
    public async Task AddNonPrescriptionItemAsync_PrescriptionMedicine_IsRejected()
    {
        var db = DbWithMedicines();
        var cart = new CartService(db, Mock.Of<IPrescriptionCleanupService>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            cart.AddNonPrescriptionItemAsync(PatientId, new AddCartItemRequestDto { MedicineId = 2, Quantity = 1 }, CancellationToken.None));

        Assert.Empty(db.CartItems);
    }

    // removing a pending prescription item deletes the prescription too
    [Fact]
    public async Task RemoveItemAsync_PendingPrescriptionItem_CallsPrescriptionCleanup()
    {
        var db = DbWithMedicines();
        db.Carts.Add(new Cart
        {
            CustomerId = PatientId,
            Items = { new CartItem { Id = 9, MedicineId = 2, Quantity = 1,
                Prescription = new Prescription { Id = 4, MedicineId = 2, UploadedBy = PatientId, Status = "PendingVerification" } } }
        });
        db.SaveChanges();
        var cleanup = new Mock<IPrescriptionCleanupService>();

        await new CartService(db, cleanup.Object).RemoveItemAsync(PatientId, 9, CancellationToken.None);

        cleanup.Verify(c => c.DeletePendingAsync(PatientId, 4, It.IsAny<CancellationToken>()), Times.Once);
    }
}
