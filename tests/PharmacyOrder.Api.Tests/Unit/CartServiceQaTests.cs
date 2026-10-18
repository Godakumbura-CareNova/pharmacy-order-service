using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>GCN-30 unit tests 1–6 for CartService (test 7 is the controller).</summary>
public class CartServiceQaTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly CancellationToken None = CancellationToken.None;

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task<Medicine> SeedMedicineAsync(AppDbContext db, string name, decimal price, int stock = 10, bool prescription = false)
    {
        var medicine = new Medicine
        {
            Name = name, Description = $"{name} description", Category = new Category { Name = $"Cat-{name}" },
            Price = price, StockQuantity = stock, PrescriptionRequired = prescription, IsActive = true
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine;
    }

    private static Task<CartResponseDto> AddAsync(CartService service, Medicine medicine, int quantity, Guid? customer = null) =>
        service.AddNonPrescriptionItemAsync(customer ?? Customer, new AddCartItemRequestDto { MedicineId = medicine.Id, Quantity = quantity }, None);

    // 1 — Scenario 1: the cart lists every item with quantity, line total and the running cart total,
    //     grouping prescription items by verification status (pending / verified / needs attention)
    [Fact]
    public async Task GetCartAsync_ReturnsAllItemsGroupedWithQuantitiesLineTotalsAndCartTotal()
    {
        await using var db = CreateDb();
        var paracetamol = await SeedMedicineAsync(db, "Paracetamol", 150m);
        var vitaminC = await SeedMedicineAsync(db, "Vitamin C", 300m);
        var antibiotic = await SeedMedicineAsync(db, "Amoxicillin", 400m, prescription: true);
        var pending = new Prescription { MedicineId = antibiotic.Id, UploadedBy = Customer, Quantity = 1, Status = "PendingVerification", FilePath = "prescriptions/a.pdf", OriginalFileName = "a.pdf" };
        var rejected = new Prescription { MedicineId = antibiotic.Id, UploadedBy = Customer, Quantity = 2, Status = "Rejected", FilePath = "prescriptions/b.pdf", OriginalFileName = "b.pdf" };
        db.Prescriptions.AddRange(pending, rejected);
        await db.SaveChangesAsync();
        var service = new CartService(db);
        await AddAsync(service, paracetamol, 2);
        await AddAsync(service, vitaminC, 1);
        await service.AddPrescriptionItemAsync(Customer, pending, None);
        await service.AddPrescriptionItemAsync(Customer, pending, None); // same prescription twice → still one line
        await service.AddPrescriptionItemAsync(Customer, rejected, None);

        var cart = await service.GetCartAsync(Customer, None);

        Assert.Equal(2, cart.ReadyItems.Count);
        var line = cart.ReadyItems.Single(i => i.MedicineName == "Paracetamol");
        Assert.Equal(2, line.Quantity);
        Assert.Equal(150m, line.UnitPrice);
        Assert.Equal(300m, line.LineTotal);
        Assert.Equal(600m, cart.ReadySubtotal);
        Assert.Equal("PendingVerification", Assert.Single(cart.PendingItems).PrescriptionStatus);
        Assert.Equal(400m, cart.PendingSubtotal);
        Assert.Equal("Rejected", Assert.Single(cart.AttentionItems).PrescriptionStatus);
        Assert.Equal(800m, cart.AttentionSubtotal);
        Assert.Equal(1800m, cart.CartTotal);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AddPrescriptionItemAsync(Guid.NewGuid(), pending, None));
    }

    // 2 — adding the same medicine twice merges into one line and the total follows
    [Fact]
    public async Task AddNonPrescriptionItemAsync_SameMedicineTwice_MergesQuantityIntoOneLine()
    {
        await using var db = CreateDb();
        var medicine = await SeedMedicineAsync(db, "Paracetamol", 150m, stock: 10);
        var service = new CartService(db);

        await AddAsync(service, medicine, 2);
        var cart = await AddAsync(service, medicine, 3);

        var line = Assert.Single(cart.ReadyItems);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(750m, cart.CartTotal);
        Assert.Equal(1, await db.CartItems.CountAsync());
    }

    // 3 — prescription medicines cannot be added directly, and adding beyond stock is refused
    [Fact]
    public async Task AddNonPrescriptionItemAsync_PrescriptionMedicineOrOverStock_IsRejected()
    {
        await using var db = CreateDb();
        var antibiotic = await SeedMedicineAsync(db, "Amoxicillin", 400m, prescription: true);
        var limited = await SeedMedicineAsync(db, "Limited", 100m, stock: 2);
        var service = new CartService(db);

        var prescriptionError = await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(service, antibiotic, 1));
        var stockError = await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(service, limited, 3));

        Assert.Equal("Prescription medicines must be added through prescription upload.", prescriptionError.Message);
        Assert.Equal("Quantity must be between 1 and 2.", stockError.Message);
        Assert.Empty(db.CartItems);
    }

    // 4 — Scenario 2: changing a quantity within stock recalculates the total
    [Fact]
    public async Task UpdateItemAsync_WithinStock_UpdatesQuantityAndTotal()
    {
        await using var db = CreateDb();
        var medicine = await SeedMedicineAsync(db, "Paracetamol", 150m, stock: 10);
        var service = new CartService(db);
        var itemId = (await AddAsync(service, medicine, 1)).ReadyItems.Single().Id;

        var cart = await service.UpdateItemAsync(Customer, itemId, new UpdateCartItemRequestDto { Quantity = 4 }, None);

        var line = Assert.Single(cart.ReadyItems);
        Assert.Equal(4, line.Quantity);
        Assert.Equal(600m, line.LineTotal);
        Assert.Equal(600m, cart.CartTotal);
    }

    // 5 — "Updated quantity exceeds available stock → error message and prevent the update"
    [Fact]
    public async Task UpdateItemAsync_AboveStockOrBelowOne_IsRejectedAndCartUnchanged()
    {
        await using var db = CreateDb();
        var medicine = await SeedMedicineAsync(db, "Paracetamol", 150m, stock: 3);
        var service = new CartService(db);
        var itemId = (await AddAsync(service, medicine, 2)).ReadyItems.Single().Id;

        var tooMany = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateItemAsync(Customer, itemId, new UpdateCartItemRequestDto { Quantity = 4 }, None));
        var zero = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateItemAsync(Customer, itemId, new UpdateCartItemRequestDto { Quantity = 0 }, None));

        Assert.Equal("Quantity must be between 1 and 3.", tooMany.Message);
        Assert.Equal("Quantity must be between 1 and 3.", zero.Message);
        Assert.Equal(2, (await service.GetCartAsync(Customer, None)).ReadyItems.Single().Quantity);
    }

    // 6 — Scenario 3: removing an item recalculates the total; only the owner can touch their items
    [Fact]
    public async Task RemoveItemAsync_RemovesItemAndRecalculatesTotal_OwnerOnly()
    {
        await using var db = CreateDb();
        var paracetamol = await SeedMedicineAsync(db, "Paracetamol", 150m);
        var vitaminC = await SeedMedicineAsync(db, "Vitamin C", 300m);
        var service = new CartService(db);
        await AddAsync(service, paracetamol, 2);
        var vitaminLine = (await AddAsync(service, vitaminC, 1)).ReadyItems.Single(i => i.MedicineName == "Vitamin C");

        var stranger = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RemoveItemAsync(Guid.NewGuid(), vitaminLine.Id, None));
        var cart = await service.RemoveItemAsync(Customer, vitaminLine.Id, None);
        var emptied = await service.RemoveItemAsync(Customer, cart.ReadyItems.Single().Id, None);

        Assert.Equal("Cart item was not found.", stranger.Message);
        Assert.Equal("Paracetamol", Assert.Single(cart.ReadyItems).MedicineName);
        Assert.Equal(300m, cart.CartTotal);
        Assert.Empty(emptied.ReadyItems);
        Assert.Equal(0m, emptied.CartTotal);
    }
}
