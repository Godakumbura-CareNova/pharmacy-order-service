using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>
/// GCN-25 unit tests for the customer catalogue service.
/// Positive tests P1–P6 live here (P7 is the controller test); negative tests N1–N5 are at the bottom.
/// </summary>
public class CatalogueServiceTests
{
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static Medicine Med(string name, Category category, decimal price = 100m, int stock = 10, bool active = true, string description = "General use") => new()
    {
        Name = name, Description = description, Category = category, Price = price, IsActive = active, StockQuantity = stock
    };

    /// <summary>Two categories, four active medicines (one out of stock) and one archived medicine.</summary>
    private static async Task<(Category pain, Category vitamins)> SeedCatalogueAsync(AppDbContext db)
    {
        var pain = new Category { Name = "Pain Relief" };
        var vitamins = new Category { Name = "Vitamins" };
        db.Medicines.AddRange(
            Med("Paracetamol", pain, 150m, stock: 20, description: "Fever and headache tablets"),
            Med("Ibuprofen", pain, 220m, stock: 0),
            Med("Vitamin C", vitamins, 300m, stock: 5),
            Med("Zinc", vitamins, 180m, stock: 8),
            Med("Archived Paracetamol", pain, 100m, active: false));
        await db.SaveChangesAsync();
        return (pain, vitamins);
    }

    // ───────────────────────────── POSITIVE ─────────────────────────────

    // P1 — full catalogue with the basic details a customer needs
    [Fact]
    public async Task GetMedicines_NoFilters_ReturnsAllActiveMedicinesWithBasicDetailsOrderedByName()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);

        var result = await new CatalogueService(db).GetMedicinesAsync(null, null, 1, 12);

        Assert.Equal(new[] { "Ibuprofen", "Paracetamol", "Vitamin C", "Zinc" }, result.Items.Select(i => i.Name).ToArray());
        Assert.Equal(4, result.TotalCount);
        var paracetamol = result.Items.Single(i => i.Name == "Paracetamol");
        Assert.Equal(150m, paracetamol.Price);
        Assert.Equal("Pain Relief", paracetamol.CategoryName);
        Assert.Equal(20, paracetamol.StockQuantity);
        Assert.Equal("Available", paracetamol.AvailabilityStatus);
        Assert.Equal("Fever and headache tablets", paracetamol.Description);
    }

    // P2 — Scenario 1: search by name (case-insensitive, whitespace ignored, also matches description)
    [Fact]
    public async Task GetMedicines_SearchByName_IsCaseInsensitiveTrimmedAndMatchesDescription()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);
        var service = new CatalogueService(db);

        var byName = await service.GetMedicinesAsync("  PARACET ", null, 1, 12);
        var byDescription = await service.GetMedicinesAsync("headache", null, 1, 12);

        Assert.Equal("Paracetamol", Assert.Single(byName.Items).Name);
        Assert.Equal("Paracetamol", Assert.Single(byDescription.Items).Name);
    }

    // P3 — Scenario 2: category filter
    [Fact]
    public async Task GetMedicines_FilterByCategory_ReturnsOnlyThatCategory()
    {
        await using var db = CreateDb();
        var (_, vitamins) = await SeedCatalogueAsync(db);

        var result = await new CatalogueService(db).GetMedicinesAsync(null, vitamins.Id, 1, 12);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(vitamins.Id, item.CategoryId));
        Assert.All(result.Items, item => Assert.Equal("Vitamins", item.CategoryName));
    }

    // P4 — search and category applied together
    [Fact]
    public async Task GetMedicines_SearchAndCategoryTogether_NarrowsResults()
    {
        await using var db = CreateDb();
        var (pain, vitamins) = await SeedCatalogueAsync(db);
        var service = new CatalogueService(db);

        var inPain = await service.GetMedicinesAsync("ibu", pain.Id, 1, 12);
        var inVitamins = await service.GetMedicinesAsync("ibu", vitamins.Id, 1, 12);

        Assert.Equal("Ibuprofen", Assert.Single(inPain.Items).Name);
        Assert.Empty(inVitamins.Items);
    }

    // P5 — pagination: 13 medicines split into a page of 12 and a page of 1
    [Fact]
    public async Task GetMedicines_Pagination_SplitsCatalogueIntoTwelveItemPages()
    {
        await using var db = CreateDb();
        var category = new Category { Name = "Bulk" };
        db.Medicines.AddRange(Enumerable.Range(1, 13).Select(i => Med($"Medicine {i:00}", category)));
        await db.SaveChangesAsync();
        var service = new CatalogueService(db);

        var first = await service.GetMedicinesAsync(null, null, 1, 12);
        var second = await service.GetMedicinesAsync(null, null, 2, 12);

        Assert.Equal(12, first.Items.Count);
        Assert.Equal(1, second.Items.Count);
        Assert.Equal("Medicine 13", second.Items[0].Name);
        Assert.Equal(13, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, second.Page);
    }

    // P6 — stock availability status and category list
    [Fact]
    public async Task GetMedicines_AndCategories_ReportStockStatusAndOnlyCategoriesWithActiveMedicines()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);
        db.Categories.Add(new Category { Name = "Empty Category" });
        db.Medicines.Add(Med("Retired Tonic", new Category { Name = "Retired Only" }, active: false));
        await db.SaveChangesAsync();
        var service = new CatalogueService(db);

        var medicines = await service.GetMedicinesAsync(null, null, 1, 12);
        var categories = await service.GetCategoriesAsync();

        Assert.Equal("Out of stock", medicines.Items.Single(i => i.Name == "Ibuprofen").AvailabilityStatus);
        Assert.Equal("Available", medicines.Items.Single(i => i.Name == "Vitamin C").AvailabilityStatus);
        Assert.Equal(new[] { "Pain Relief", "Vitamins" }, categories.Select(c => c.Name).ToArray());
    }

    // ───────────────────────────── NEGATIVE ─────────────────────────────

    // N1 — "Search field left empty → show the full catalogue"
    [Fact]
    public async Task GetMedicines_EmptyOrWhitespaceSearch_ReturnsFullCatalogue()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);
        var service = new CatalogueService(db);

        var empty = await service.GetMedicinesAsync("", null, 1, 12);
        var whitespace = await service.GetMedicinesAsync("   ", null, 1, 12);

        Assert.Equal(4, empty.TotalCount);
        Assert.Equal(4, whitespace.TotalCount);
    }

    // N2 — Scenario 3: nothing matches
    [Fact]
    public async Task GetMedicines_NoMatchingSearch_ReturnsEmptyPageWithZeroTotals()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);

        var result = await new CatalogueService(db).GetMedicinesAsync("does-not-exist-xyz", null, 1, 12);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    // N3 — unknown category id
    [Fact]
    public async Task GetMedicines_UnknownCategory_ReturnsNoResults()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);

        var result = await new CatalogueService(db).GetMedicinesAsync(null, 9_999, 1, 12);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    // N4 — deactivated medicines never leak into the customer catalogue, even when searched for directly
    [Fact]
    public async Task GetMedicines_InactiveMedicines_AreExcludedEvenWhenSearchedByName()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);

        var result = await new CatalogueService(db).GetMedicinesAsync("archived", null, 1, 12);

        Assert.Empty(result.Items);
        Assert.DoesNotContain(await db.Medicines.ToListAsync(), m => m.IsActive && m.Name == "Archived Paracetamol");
    }

    // N5 — bad paging input is clamped instead of failing
    [Fact]
    public async Task GetMedicines_InvalidPageOrPageSize_IsClampedToSafeValues()
    {
        await using var db = CreateDb();
        await SeedCatalogueAsync(db);
        var service = new CatalogueService(db);

        var zeroPage = await service.GetMedicinesAsync(null, null, 0, 12);
        var negativePage = await service.GetMedicinesAsync(null, null, -5, 12);
        var hugePageSize = await service.GetMedicinesAsync(null, null, 1, 500);
        var zeroPageSize = await service.GetMedicinesAsync(null, null, 1, 0);

        Assert.Equal(1, zeroPage.Page);
        Assert.Equal(1, negativePage.Page);
        Assert.Equal(12, hugePageSize.PageSize);
        Assert.Equal(1, zeroPageSize.PageSize);
        Assert.Single(zeroPageSize.Items);
        Assert.Equal(4, zeroPageSize.TotalPages);
    }

    // N6 - REQUIREMENT (user story: "Filter medicines by stock availability").
    //      The service contract must accept a stock-availability filter. It cannot be called in code (it would not
    //      compile), so the signature is inspected instead. EXPECTED TO FAIL on the current build.
    [Fact]
    public void GetMedicines_ContractExposesStockAvailabilityFilter()
    {
        var parameters = typeof(PharmacyOrder.Api.Services.Interfaces.ICatalogueService)
            .GetMethod(nameof(CatalogueService.GetMedicinesAsync))!
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToArray();

        Assert.True(parameters.Any(name => name.Contains("stock", StringComparison.OrdinalIgnoreCase)
                                        || name.Contains("availab", StringComparison.OrdinalIgnoreCase)),
            $"Stock-availability filter is not implemented: GetMedicinesAsync only accepts ({string.Join(", ", parameters)}).");
    }
}
