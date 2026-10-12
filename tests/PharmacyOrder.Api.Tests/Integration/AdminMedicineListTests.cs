using System.Net.Http.Json;
using System.Text.Json;

namespace PharmacyOrder.Api.Tests.Integration;

public class AdminMedicineListTests
{
    [Theory]
    [InlineData(null, 401)] [InlineData("Patient", 403)] [InlineData("Doctor", 403)]
    [InlineData("PharmacyStaff", 403)] [InlineData("Admin", 200)]
    public async Task OnlyAdminCanList(string? role, int expected)
    {
        using var factory = new CartApiFactory(); using var client = factory.CreateClientAs(role);
        Assert.Equal(expected, (int)(await client.GetAsync("/api/admin/medicines")).StatusCode);
    }

    [Fact]
    public async Task IncludesInactiveStockAndSearchesWithBoundedPages()
    {
        using var factory = new CartApiFactory();
        await factory.SeedMedicineAsync("Find A", stock: 0, active: false);
        await factory.SeedMedicineAsync("Find B", stock: 12);
        await factory.SeedMedicineAsync("Other", stock: 2);
        using var client = factory.CreateClientAs("Admin");
        var response = await client.GetAsync("/api/admin/medicines?search=Find&page=1&pageSize=1");
        Assert.Equal(200, (int)response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal("Find A", item.GetProperty("name").GetString());
        Assert.False(item.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, item.GetProperty("stockQuantity").GetInt32());
        Assert.Equal(400, (int)(await client.GetAsync("/api/admin/medicines?pageSize=101")).StatusCode);
        Assert.Equal(403, (int)(await client.PostAsJsonAsync("/api/staff/medicines", new { })).StatusCode);
    }
}
