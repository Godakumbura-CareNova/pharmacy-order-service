using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Tests.Integration;

public class AdminDashboardTests
{
    [Fact]
    public async Task CountsAllOrdersAndBoundsDeterministicPreviewsAndStock()
    {
        using var factory = new CartApiFactory();
        var threshold = factory.Services.GetRequiredService<IOptions<KafkaSettings>>().Value.LowStockThreshold;
        await factory.QueryAsync(async db => {
            for (var i = 1; i <= 7; i++) db.Orders.Add(new Order { Id = i, CustomerId = Guid.NewGuid(), RequestId = Guid.NewGuid(),
                OrderDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), TotalAmount = 100,
                Status = i == 7 ? OrderStatus.ReadyForCollection : OrderStatus.Pending });
            return await db.SaveChangesAsync();
        });
        for (var i = 0; i < 7; i++) await factory.SeedMedicineAsync($"Low-{i}", stock: i == 6 ? threshold : 0);
        await factory.SeedMedicineAsync("High", stock: threshold + 1);
        await factory.SeedMedicineAsync("Inactive", stock: 0, active: false);
        using var client = factory.CreateClientAs("Admin");
        var response = await client.GetAsync("/api/admin/dashboard/operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, json.GetProperty("totalOrders").GetInt32());
        Assert.Equal(6, json.GetProperty("orderStatusCounts").GetProperty("Pending").GetInt32());
        Assert.Equal(new[] { 7, 6, 5, 4, 3 }, json.GetProperty("recentOrders").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToArray());
        var stock = json.GetProperty("lowStock");
        Assert.Equal(7, stock.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, stock.GetProperty("items").GetArrayLength());
        Assert.All(stock.GetProperty("items").EnumerateArray(), x => Assert.Equal(0, x.GetProperty("quantity").GetInt32()));
        Assert.DoesNotContain("Inactive", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/staff/orders/1/status", new { status = "Collected" })).StatusCode);
    }

    [Theory]
    [InlineData(null, 401)] [InlineData("Patient", 403)] [InlineData("User", 403)] [InlineData("Doctor", 403)]
    [InlineData("PharmacyStaff", 403)] [InlineData("Pharmacy Staff", 403)] [InlineData("Admin", 200)]
    public async Task AdminOnlyAndEmpty(string? role, int status)
    {
        using var factory = new CartApiFactory(); using var client = factory.CreateClientAs(role);
        var response = await client.GetAsync("/api/admin/dashboard/operations");
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 200)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, json.GetProperty("totalOrders").GetInt32());
            Assert.Equal(0, json.GetProperty("recentOrders").GetArrayLength());
            Assert.Equal(0, json.GetProperty("lowStock").GetProperty("totalCount").GetInt32());
        }
    }
}
