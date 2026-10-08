using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace PharmacyOrder.Api.Tests.Integration;
public class PublicMedicinesTests
{
    [Fact]
    public async Task AnonymousBrowsingOnlyShowsActiveMedicinesWithoutInternalStockCounts()
    {
        using var factory = new CatalogueApiFactory();
        await factory.SeedCategoryAsync("Vitamins", ("Vitamin C", 200, 10, true), ("Hidden medicine", 100, 3, false));
        using var client = factory.CreateClientAs(null);
        var response = await client.GetAsync("/api/public/medicines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items");
        Assert.Single(items.EnumerateArray());
        var medicine = items[0];
        Assert.Equal("Vitamin C", medicine.GetProperty("name").GetString());
        Assert.False(medicine.TryGetProperty("stockQuantity", out _));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/public/medicines/{medicine.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/catalogue/medicines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/catalogue/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/catalogue/medicines/{medicine.GetProperty("id").GetInt32()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/cart/items", new { medicineId = medicine.GetProperty("id").GetInt32(), quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/public/medicines/999999")).StatusCode);
    }
    [Fact]
    public async Task InvalidPaginationIsRejected()
    {
        using var factory = new CatalogueApiFactory();
        using var client = factory.CreateClientAs(null);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/public/medicines?page=0")).StatusCode);
    }
}
