using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>
/// GCN-26 unit tests for the medicine details service (GetMedicineAsync).
/// Positive P1–P3 and negative N1–N2 live here; the controller tests hold P4 and N3;
/// the quantity-selector tests (P5–P8, N4–N6) are vitest tests in the frontend.
/// </summary>
public class CatalogueServiceTests
{
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task<Medicine> SeedAsync(AppDbContext db, int stock = 25, bool active = true, bool prescription = false, string? imagePath = null)
    {
        var medicine = new Medicine
        {
            Name = "Amoxicillin 500mg",
            Description = "Broad-spectrum antibiotic capsules",
            Category = new Category { Name = "Antibiotics" },
            Price = 450.75m,
            StockQuantity = stock,
            IsActive = active,
            PrescriptionRequired = prescription,
            ImagePath = imagePath
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine;
    }

    // ───────────────────────────── POSITIVE ─────────────────────────────

    // P1 — detail view: name, description, category, price
    [Fact]
    public async Task GetMedicineAsync_ReturnsNameDescriptionCategoryAndPrice()
    {
        await using var db = CreateDb();
        var medicine = await SeedAsync(db);

        var result = await new CatalogueService(db).GetMedicineAsync(medicine.Id);

        Assert.Equal(medicine.Id, result.Id);
        Assert.Equal("Amoxicillin 500mg", result.Name);
        Assert.Equal("Broad-spectrum antibiotic capsules", result.Description);
        Assert.Equal("Antibiotics", result.CategoryName);
        Assert.Equal(medicine.CategoryId, result.CategoryId);
        Assert.Equal(450.75m, result.Price);
    }

    // P2 — detail view: available stock (the quantity selector's upper limit) and availability status
    [Fact]
    public async Task GetMedicineAsync_ExposesAvailableStockAsQuantityLimit()
    {
        await using var db = CreateDb();
        var medicine = await SeedAsync(db, stock: 7);

        var result = await new CatalogueService(db).GetMedicineAsync(medicine.Id);

        Assert.Equal(7, result.StockQuantity);
        Assert.Equal("Available", result.AvailabilityStatus);
    }

    // P3 — detail view: prescription requirement status and image path are passed through
    [Fact]
    public async Task GetMedicineAsync_ReportsPrescriptionRequirementAndImage()
    {
        await using var db = CreateDb();
        var prescriptionOnly = await SeedAsync(db, prescription: true, imagePath: "/uploads/medicines/amox.png");

        var result = await new CatalogueService(db).GetMedicineAsync(prescriptionOnly.Id);

        Assert.True(result.PrescriptionRequired);
        Assert.Equal("/uploads/medicines/amox.png", result.ImagePath);
    }

    // ───────────────────────────── NEGATIVE ─────────────────────────────

    // N1 — unknown medicine id → not found ("Medicine details fail to load")
    [Fact]
    public async Task GetMedicineAsync_UnknownId_ThrowsMedicineUnavailable()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => new CatalogueService(db).GetMedicineAsync(999_999));

        Assert.Equal("Medicine is unavailable.", exception.Message);
    }

    // N2 — deactivated or out-of-stock medicines cannot be viewed for purchase (prevents overselling)
    [Fact]
    public async Task GetMedicineAsync_InactiveOrOutOfStock_ThrowsMedicineUnavailable()
    {
        await using var db = CreateDb();
        var inactive = await SeedAsync(db, stock: 10, active: false);
        var outOfStock = await SeedAsync(db, stock: 0, active: true);
        var service = new CatalogueService(db);

        var inactiveError = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetMedicineAsync(inactive.Id));
        var stockError = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetMedicineAsync(outOfStock.Id));

        Assert.Equal("Medicine is unavailable.", inactiveError.Message);
        Assert.Equal("Medicine is unavailable.", stockError.Message);
    }
}
