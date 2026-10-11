using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Tests.Integration;

public class PharmacyDashboardTests
{
    [Fact]
    public async Task PatientOrders_ExcludeForeignRecords_AndLimitFiveButCountAll()
    {
        using var factory = new CartApiFactory();
        var patient = Guid.NewGuid();
        await factory.QueryAsync(async db =>
        {
            for (var i = 1; i <= 8; i++) db.Orders.Add(new Order
            {
                CustomerId = i == 8 ? Guid.NewGuid() : patient, RequestId = Guid.NewGuid(),
                OrderDate = new DateTime(2026, 10, i, 0, 0, 0, DateTimeKind.Utc), TotalAmount = 100 * i
            });
            return await db.SaveChangesAsync();
        });
        using var client = factory.CreatePatientClient(patient);
        var response = await client.GetAsync("/api/patient/dashboard/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7, json.RootElement.GetProperty("totalCount").GetInt32());
        var rows = json.RootElement.GetProperty("recentOrders").EnumerateArray().ToArray();
        Assert.Equal(5, rows.Length);
        Assert.Equal(new[] { 700m, 600m, 500m, 400m, 300m }, rows.Select(r => r.GetProperty("totalAmount").GetDecimal()));
    }

    [Fact]
    public async Task Operations_UseRealQueues_AndActiveStockThreshold()
    {
        using var factory = new CartApiFactory();
        await factory.QueryAsync(async db =>
        {
            var category = new Category { Name = "Dashboard" }; db.Categories.Add(category);
            var medicines = new[] { ("Empty", 0, true), ("Threshold", 10, true), ("Available", 11, true), ("Inactive", 1, false) }
                .Select(x => new Medicine { Name = x.Item1, StockQuantity = x.Item2, IsActive = x.Item3, Category = category }).ToArray();
            db.Medicines.AddRange(medicines);
            db.Prescriptions.Add(new Prescription { Medicine = medicines[0], UploadedBy = Guid.NewGuid(), Quantity = 1, UploadedAt = DateTime.UtcNow });
            foreach (var status in new[] { OrderStatus.Pending, OrderStatus.ReadyForCollection, OrderStatus.Collected, OrderStatus.Rejected })
                db.Orders.Add(new Order { CustomerId = Guid.NewGuid(), RequestId = Guid.NewGuid(), Status = status, OrderDate = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        });
        using var staff = factory.CreateClientAs("PharmacyStaff");
        var response = await staff.GetAsync("/api/staff/dashboard/operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var key in new[] { "newOrders", "readyForCollection", "pendingPrescriptions" })
            Assert.Equal(1, json.RootElement.GetProperty(key).GetProperty("totalCount").GetInt32());
        var stock = json.RootElement.GetProperty("lowStock");
        Assert.Equal(2, stock.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "Empty", "Threshold" }, stock.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("medicineName").GetString()));
    }

    [Theory]
    [InlineData(null, "/api/patient/dashboard/orders", HttpStatusCode.Unauthorized)]
    [InlineData("Doctor", "/api/patient/dashboard/orders", HttpStatusCode.Forbidden)]
    [InlineData(null, "/api/staff/dashboard/operations", HttpStatusCode.Unauthorized)]
    [InlineData("Patient", "/api/staff/dashboard/operations", HttpStatusCode.Forbidden)]
    [InlineData("Admin", "/api/staff/dashboard/operations", HttpStatusCode.Forbidden)]
    public async Task Dashboards_EnforceRoles(string? role, string route, HttpStatusCode expected)
    {
        using var factory = new CartApiFactory();
        using var client = factory.CreateClientAs(role);
        Assert.Equal(expected, (await client.GetAsync(route)).StatusCode);
    }
}
