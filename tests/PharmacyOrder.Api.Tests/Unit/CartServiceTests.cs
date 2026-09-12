using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class CartServiceTests
{
    [Fact]
    public async Task AddNonPrescriptionItemAsync_IncrementsExistingItemWithinAvailableStock()
    {
        await using var db = CreateDbContext();
        var medicine = new Medicine
        {
            Name = "Ibuprofen 200mg",
            Description = "Pain relief",
            Price = 200m,
            IsActive = true,
            StockQuantity = 3,
            PrescriptionRequired = false
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        var service = new CartService(db);
        var patientId = Guid.NewGuid();

        await service.AddNonPrescriptionItemAsync(patientId, new AddCartItemRequestDto
        {
            MedicineId = medicine.Id,
            Quantity = 1
        }, CancellationToken.None);
        var response = await service.AddNonPrescriptionItemAsync(patientId, new AddCartItemRequestDto
        {
            MedicineId = medicine.Id,
            Quantity = 2
        }, CancellationToken.None);

        var item = await db.CartItems.SingleAsync();
        Assert.Equal(3, item.Quantity);
        Assert.Null(item.PrescriptionId);
        var responseItem = Assert.Single(response.ReadyItems);
        Assert.Equal(3, responseItem.Quantity);
        Assert.Equal(600m, response.ReadySubtotal);
    }

    [Fact]
    public async Task GetCartAsync_PlacesPendingPrescriptionItemInPendingSection()
    {
        var patientId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var medicine = new Medicine
        {
            Name = "Amoxicillin 250mg",
            Description = "Antibiotic",
            Price = 300m,
            IsActive = true,
            StockQuantity = 10,
            PrescriptionRequired = true
        };
        var prescription = new Prescription
        {
            UploadedBy = patientId,
            Medicine = medicine,
            Quantity = 2,
            Status = "PendingVerification",
            FilePath = "prescriptions/doctor-note.pdf",
            OriginalFileName = "doctor-note.pdf",
            UploadedAt = DateTime.UtcNow
        };
        var cart = new Cart { CustomerId = patientId };
        cart.Items.Add(new CartItem { Medicine = medicine, Prescription = prescription, Quantity = 2 });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();
        var service = new CartService(db);

        var response = await service.GetCartAsync(patientId, CancellationToken.None);

        Assert.Empty(response.ReadyItems);
        var item = Assert.Single(response.PendingItems);
        Assert.Equal("PendingVerification", item.PrescriptionStatus);
        Assert.Equal(600m, response.PendingSubtotal);
        Assert.Equal(600m, response.CartTotal);
    }

    [Fact]
    public async Task AddNonPrescriptionItemAsync_RejectsQuantityAboveAvailableStock()
    {
        await using var db = CreateDbContext();
        var medicine = new Medicine
        {
            Name = "Paracetamol 500mg",
            Description = "Pain relief",
            Price = 150m,
            IsActive = true,
            StockQuantity = 2,
            PrescriptionRequired = false
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        var service = new CartService(db);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.AddNonPrescriptionItemAsync(
            Guid.NewGuid(),
            new AddCartItemRequestDto { MedicineId = medicine.Id, Quantity = 3 },
            CancellationToken.None));

        Assert.Equal("Quantity must be between 1 and 2.", error.Message);
        Assert.Empty(db.CartItems);
    }

    [Fact]
    public async Task AddNonPrescriptionItemAsync_RejectsPrescriptionMedicine()
    {
        await using var db = CreateDbContext();
        var medicine = new Medicine
        {
            Name = "Amoxicillin 250mg",
            Description = "Antibiotic",
            Price = 300m,
            IsActive = true,
            StockQuantity = 10,
            PrescriptionRequired = true
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        var service = new CartService(db);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.AddNonPrescriptionItemAsync(
            Guid.NewGuid(),
            new AddCartItemRequestDto { MedicineId = medicine.Id, Quantity = 1 },
            CancellationToken.None));

        Assert.Equal("Prescription medicines must be added through prescription upload.", error.Message);
        Assert.Empty(db.CartItems);
    }

    [Fact]
    public async Task UpdateItemAsync_RejectsAnotherPatientsCartItem()
    {
        await using var db = CreateDbContext();
        var medicine = new Medicine { Name = "Ibuprofen 200mg", Description = "Pain relief", Price = 200m, IsActive = true, StockQuantity = 8 };
        var cart = new Cart { CustomerId = Guid.NewGuid() };
        cart.Items.Add(new CartItem { Medicine = medicine, Quantity = 1 });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();
        var service = new CartService(db);

        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateItemAsync(
            Guid.NewGuid(),
            cart.Items.Single().Id,
            new UpdateCartItemRequestDto { Quantity = 2 },
            CancellationToken.None));

        Assert.Equal("Cart item was not found.", error.Message);
    }

    [Fact]
    public async Task UpdateItemAsync_RejectsQuantityAboveAvailableStock()
    {
        var patientId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var medicine = new Medicine { Name = "Ibuprofen 200mg", Description = "Pain relief", Price = 200m, IsActive = true, StockQuantity = 2 };
        var cart = new Cart { CustomerId = patientId };
        cart.Items.Add(new CartItem { Medicine = medicine, Quantity = 1 });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();
        var service = new CartService(db);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateItemAsync(
            patientId,
            cart.Items.Single().Id,
            new UpdateCartItemRequestDto { Quantity = 3 },
            CancellationToken.None));

        Assert.Equal("Quantity must be between 1 and 2.", error.Message);
    }

    [Fact]
    public async Task RemoveItemAsync_RemovesOnlyThePatientsItem()
    {
        var patientId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var medicine = new Medicine { Name = "Ibuprofen 200mg", Description = "Pain relief", Price = 200m, IsActive = true, StockQuantity = 8 };
        var cart = new Cart { CustomerId = patientId };
        cart.Items.Add(new CartItem { Medicine = medicine, Quantity = 1 });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();
        var service = new CartService(db);

        var response = await service.RemoveItemAsync(patientId, cart.Items.Single().Id, CancellationToken.None);

        Assert.Empty(db.CartItems);
        Assert.Empty(response.ReadyItems);
        Assert.Equal(0m, response.CartTotal);
    }

    [Fact]
    public async Task AddPrescriptionItemAsync_AddsOwnedPendingPrescriptionToPendingSection()
    {
        var patientId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var medicine = new Medicine { Name = "Amoxicillin 250mg", Description = "Antibiotic", Price = 300m, IsActive = true, StockQuantity = 10, PrescriptionRequired = true };
        var prescription = new Prescription
        {
            UploadedBy = patientId,
            Medicine = medicine,
            Quantity = 2,
            Status = "PendingVerification",
            FilePath = "prescriptions/doctor-note.pdf",
            OriginalFileName = "doctor-note.pdf",
            UploadedAt = DateTime.UtcNow
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        var service = new CartService(db);

        var response = await service.AddPrescriptionItemAsync(patientId, prescription, CancellationToken.None);

        var item = Assert.Single(response.PendingItems);
        Assert.Equal(prescription.Id, item.PrescriptionId);
        Assert.Equal(2, item.Quantity);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
