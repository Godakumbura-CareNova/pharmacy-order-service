using System.Net;
using System.Net.Http.Json;
using PharmacyOrder.Api.DTOs.Catalogue;

namespace PharmacyOrder.Api.Tests.Integration;

public class CatalogueControllerIntegrationTests : IClassFixture<CatalogueApiFactory>
{
    private const string MedicinesUrl = "/api/catalogue/medicines";
    private const string CategoriesUrl = "/api/catalogue/categories";

    private readonly CatalogueApiFactory _factory;
    private readonly HttpClient _patient;

    public CatalogueControllerIntegrationTests(CatalogueApiFactory factory)
    {
        _factory = factory;
        _patient = factory.CreatePatientClient();
    }

    private Task<PaginatedCatalogueResponseDto?> GetPageAsync(string query = "") =>
        _patient.GetFromJsonAsync<PaginatedCatalogueResponseDto>($"{MedicinesUrl}{query}");

    // Public catalogue reads use the same responses as patient browsing.
    [Fact]
    public async Task GetMedicines_WithoutToken_Returns200()
    {
        var anonymous = _factory.CreateClientAs(role: null);

        var response = await anonymous.GetAsync(MedicinesUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Browsing is available to signed-in users of other roles too.
    [Fact]
    public async Task GetMedicines_WithNonPatientRole_Returns200()
    {
        var staff = _factory.CreateClientAs("PharmacyStaff");

        var response = await staff.GetAsync(MedicinesUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // 3 — listing returns the basic details a customer needs
    [Fact]
    public async Task GetMedicines_AsPatient_ReturnsMedicineWithNamePriceCategoryAndStockStatus()
    {
        await _factory.SeedCategoryAsync("IT-Cat-Details", ("IT-Detail-Med", 199.5m, 7, true));

        var page = await GetPageAsync("?search=IT-Detail-Med");

        var medicine = Assert.Single(page!.Items);
        Assert.Equal("IT-Detail-Med", medicine.Name);
        Assert.Equal(199.5m, medicine.Price);
        Assert.Equal("IT-Cat-Details", medicine.CategoryName);
        Assert.Equal(7, medicine.StockQuantity);
        Assert.Equal("Available", medicine.AvailabilityStatus);
    }

    // 4 — Scenario 1: search by name returns matching medicines only
    [Fact]
    public async Task GetMedicines_SearchByName_ReturnsOnlyMatches()
    {
        await _factory.SeedCategoryAsync("IT-Cat-Search", ("IT-Amoxicillin", 100m, 5, true), ("IT-Cetirizine", 100m, 5, true));

        var page = await GetPageAsync("?search=amoxi");

        Assert.Contains(page!.Items, m => m.Name == "IT-Amoxicillin");
        Assert.DoesNotContain(page.Items, m => m.Name == "IT-Cetirizine");
    }

    // 5 — Scenario 2: category filter returns only that category
    [Fact]
    public async Task GetMedicines_FilterByCategory_ReturnsOnlyThatCategory()
    {
        var target = await _factory.SeedCategoryAsync("IT-Cat-Target", ("IT-Target-A", 100m, 5, true), ("IT-Target-B", 100m, 5, true));
        await _factory.SeedCategoryAsync("IT-Cat-Other", ("IT-Other-A", 100m, 5, true));

        var page = await GetPageAsync($"?categoryId={target.Id}");

        Assert.Equal(2, page!.TotalCount);
        Assert.All(page.Items, m => Assert.Equal(target.Id, m.CategoryId));
    }

    // 6 — Scenario 3: no matches → empty page, zero totals
    [Fact]
    public async Task GetMedicines_NoMatches_ReturnsEmptyItemsAndZeroTotals()
    {
        var page = await GetPageAsync("?search=zzz-no-such-medicine-zzz");

        Assert.Empty(page!.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    // 7 — empty search field → the full catalogue
    [Fact]
    public async Task GetMedicines_EmptySearch_ReturnsFullCatalogue()
    {
        await _factory.SeedCategoryAsync("IT-Cat-Empty-Search", ("IT-Empty-Search-A", 100m, 5, true));

        var withEmpty = await GetPageAsync("?search=");
        var withoutParam = await GetPageAsync();

        Assert.True(withEmpty!.TotalCount > 0);
        Assert.Equal(withoutParam!.TotalCount, withEmpty.TotalCount);
    }

    // 8 — deactivated medicines are not visible to customers
    [Fact]
    public async Task GetMedicines_ExcludesInactiveMedicines()
    {
        await _factory.SeedCategoryAsync("IT-Cat-Inactive", ("IT-Inactive-Med", 100m, 5, false), ("IT-Active-Med", 100m, 5, true));

        var page = await GetPageAsync("?search=IT-");

        Assert.DoesNotContain(page!.Items, m => m.Name == "IT-Inactive-Med");
        Assert.Contains(page.Items, m => m.Name == "IT-Active-Med");
    }

    // 9 — pagination: page size is capped at 12 and page 2 continues the list
    [Fact]
    public async Task GetMedicines_Pagination_CapsPageSizeAtTwelveAndServesSecondPage()
    {
        await _factory.SeedCategoryAsync("IT-Cat-Paging",
            Enumerable.Range(1, 13).Select(i => ($"IT-Page-{i:00}", 100m, 5, true)).ToArray());

        var first = await GetPageAsync("?search=IT-Page-&page=1&pageSize=100");
        var second = await GetPageAsync("?search=IT-Page-&page=2&pageSize=12");

        Assert.Equal(12, first!.PageSize);
        Assert.Equal(12, first.Items.Count);
        Assert.Equal(13, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal("IT-Page-13", Assert.Single(second!.Items).Name);
    }

    // 10 — stock availability: zero stock is reported as "Out of stock"
    [Fact]
    public async Task GetMedicines_ZeroStock_IsReportedAsOutOfStock()
    {
        await _factory.SeedCategoryAsync("IT-Cat-Stock", ("IT-Stock-Zero", 100m, 0, true), ("IT-Stock-Some", 100m, 3, true));

        var page = await GetPageAsync("?search=IT-Stock-");

        Assert.Equal("Out of stock", page!.Items.Single(m => m.Name == "IT-Stock-Zero").AvailabilityStatus);
        Assert.Equal("Available", page.Items.Single(m => m.Name == "IT-Stock-Some").AvailabilityStatus);
    }

    // 11 — category dropdown lists only categories that have an active medicine
    [Fact]
    public async Task GetCategories_ReturnsOnlyCategoriesWithActiveMedicines()
    {
        var live = await _factory.SeedCategoryAsync("IT-Cat-Live", ("IT-Live-Med", 100m, 5, true));
        var retired = await _factory.SeedCategoryAsync("IT-Cat-Retired", ("IT-Retired-Med", 100m, 5, false));
        var empty = await _factory.SeedCategoryAsync("IT-Cat-No-Medicines");

        var categories = await _patient.GetFromJsonAsync<List<CatalogueCategoryResponseDto>>(CategoriesUrl);

        Assert.Contains(categories!, c => c.Id == live.Id);
        Assert.DoesNotContain(categories!, c => c.Id == retired.Id);
        Assert.DoesNotContain(categories!, c => c.Id == empty.Id);
    }

    // 13 - REQUIREMENT (user story: "Filter medicines by stock availability").
    //      Expected contract: ?inStock=true hides out-of-stock medicines (mirrors the staff ?lowStockOnly flag).
    //      EXPECTED TO FAIL on the current build: the API ignores the parameter and returns out-of-stock items.
    [Fact]
    public async Task GetMedicines_InStockOnlyFilter_ExcludesOutOfStockMedicines()
    {
        await _factory.SeedCategoryAsync("IT-Cat-Availability", ("IT-Avail-Zero", 100m, 0, true), ("IT-Avail-Some", 100m, 4, true));

        var page = await GetPageAsync("?search=IT-Avail-&inStock=true");

        Assert.Contains(page!.Items, m => m.Name == "IT-Avail-Some");
        Assert.True(page.Items.All(m => m.AvailabilityStatus == "Available"),
            "Stock-availability filter is not implemented: ?inStock=true still returned out-of-stock medicines: "
            + string.Join(", ", page.Items.Where(m => m.AvailabilityStatus != "Available").Select(m => m.Name)));
    }

    // 12 — malformed query values are rejected with 400 rather than crashing
    [Fact]
    public async Task GetMedicines_NonNumericPageOrCategory_Returns400()
    {
        var badPage = await _patient.GetAsync($"{MedicinesUrl}?page=abc");
        var badCategory = await _patient.GetAsync($"{MedicinesUrl}?categoryId=vitamins");

        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
    }
}
