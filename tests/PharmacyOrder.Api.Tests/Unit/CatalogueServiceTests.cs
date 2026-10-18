using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class CatalogueServiceTests
{
    [Fact]
    public async Task GetMedicinesAsync_ReturnsActiveSearchResultsInTwelveItemPages()
    {
        await using var db = CreateDbContext();
        var category = new Category { Name = "Pain Relief" };
        db.Categories.Add(category);
        db.Medicines.AddRange(Enumerable.Range(1, 13).Select(index => new Medicine
        {
            Name = $"Pain Tablet {index}",
            Description = "Pain relief medicine",
            Category = category,
            Price = 100m,
            IsActive = true,
            StockQuantity = index
        }));
        db.Medicines.Add(new Medicine
        {
            Name = "Archived Pain Tablet",
            Description = "Pain relief medicine",
            Category = category,
            Price = 100m,
            IsActive = false
        });
        await db.SaveChangesAsync();

        var result = await new CatalogueService(db).GetMedicinesAsync("pain", category.Id, null, 1, 12);

        Assert.Equal(12, result.Items.Count);
        Assert.Equal(13, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.DoesNotContain(result.Items, item => item.Name == "Archived Pain Tablet");
    }

    [Fact]
    public async Task GetMedicinesAsync_LabelsZeroStockMedicineAsOutOfStock()
    {
        await using var db = CreateDbContext();
        var category = new Category { Name = "Vitamins" };
        db.Medicines.Add(new Medicine
        {
            Name = "Vitamin C",
            Description = "Vitamin supplement",
            Category = category,
            Price = 200m,
            IsActive = true,
            StockQuantity = 0
        });
        await db.SaveChangesAsync();

        var result = await new CatalogueService(db).GetMedicinesAsync(null, null, null, 1, 12);

        Assert.Single(result.Items);
        Assert.Equal("Out of stock", result.Items[0].AvailabilityStatus);
    }

    [Fact]
    public async Task GetCategoriesAsync_ReturnsOnlyCategoriesWithActiveMedicines()
    {
        await using var db = CreateDbContext();
        var activeCategory = new Category { Name = "Pain Relief" };
        var inactiveCategory = new Category { Name = "Retired" };
        db.Medicines.Add(new Medicine { Name = "Paracetamol", Description = "Pain relief", Category = activeCategory, IsActive = true });
        db.Medicines.Add(new Medicine { Name = "Old medicine", Description = "Retired", Category = inactiveCategory, IsActive = false });
        await db.SaveChangesAsync();

        var categories = await new CatalogueService(db).GetCategoriesAsync();

        Assert.Collection(categories, category => Assert.Equal("Pain Relief", category.Name));
    }

    [Fact]
    public async Task GetMedicineAsync_ReturnsOnlyActiveMedicineWithStock()
    {
        await using var db = CreateDbContext();
        var category = new Category { Name = "Pain Relief" };
        var medicine = new Medicine { Name = "Ibuprofen", Description = "Pain relief", Category = category, IsActive = true, StockQuantity = 5, Price = 200m };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();

        var result = await new CatalogueService(db).GetMedicineAsync(medicine.Id);

        Assert.Equal("Ibuprofen", result.Name);
        Assert.Equal(5, result.StockQuantity);
    }

    [Fact]
    public async Task GetMedicineAsync_ReturnsActiveOutOfStockMedicineForDetails()
    {
        await using var db = CreateDbContext();
        var category = new Category { Name = "Pain Relief" };
        var medicine = new Medicine { Name = "Ibuprofen", Description = "Pain relief", Category = category, IsActive = true, StockQuantity = 0, Price = 200m };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();

        var result = await new CatalogueService(db).GetMedicineAsync(medicine.Id);

        Assert.Equal("Ibuprofen", result.Name);
        Assert.Equal(0, result.StockQuantity);
        Assert.Equal("Out of stock", result.AvailabilityStatus);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
