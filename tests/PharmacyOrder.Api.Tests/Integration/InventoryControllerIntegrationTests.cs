using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.DTOs.Inventory;
using PharmacyOrder.Api.Events;

namespace PharmacyOrder.Api.Tests.Integration;

public class InventoryControllerIntegrationTests : IClassFixture<InventoryApiFactory>
{
    private const string InventoryUrl = "/api/staff/inventory";
    private const string BatchesUrl = "/api/staff/inventory/batches";

    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _staff;

    public InventoryControllerIntegrationTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _staff = factory.CreateStaffClient();
    }

    private static StockBatchRequestDto Batch(int medicineId, string batchNumber, int quantity) => new()
    {
        MedicineId = medicineId,
        BatchNumber = batchNumber,
        Quantity = quantity,
        PurchasePrice = 80m,
        SellingPrice = 100m,
        ManufacturingDate = new DateTime(2026, 1, 1),
        ExpiryDate = new DateTime(2027, 1, 1)
    };

    private async Task<MedicineInventoryResponseDto> InventoryRowAsync(int medicineId, bool lowStockOnly = false)
    {
        var rows = await _staff.GetFromJsonAsync<List<MedicineInventoryResponseDto>>($"{InventoryUrl}?lowStockOnly={lowStockOnly}");
        return rows!.Single(r => r.MedicineId == medicineId);
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return body!["message"].ToString()!;
    }

    // 1
    [Fact]
    public async Task GetInventory_WithoutToken_Returns401()
    {
        var anonymous = _factory.CreateClientAs(role: null);

        var response = await anonymous.GetAsync(InventoryUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 2
    [Fact]
    public async Task GetInventory_WithCustomerRole_Returns403()
    {
        var customer = _factory.CreateClientAs("Customer");

        var response = await customer.GetAsync(InventoryUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 3
    [Fact]
    public async Task GetInventory_AsStaff_ReturnsMedicineTotalsAndStatus()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Inventory-Available", stock: 40);

        var row = await InventoryRowAsync(medicine.Id);

        Assert.Equal("IT-Inventory-Available", row.MedicineName);
        Assert.Equal(40, row.TotalQuantity);
        Assert.Equal("Available", row.AvailabilityStatus);
        Assert.False(row.IsLowStock);
    }

    // 4
    [Fact]
    public async Task GetInventory_LowStockOnly_ExcludesMedicinesAboveThreshold()
    {
        var healthy = await _factory.SeedMedicineAsync("IT-Healthy", stock: 50);
        var low = await _factory.SeedMedicineAsync("IT-Low", stock: 3);

        var rows = await _staff.GetFromJsonAsync<List<MedicineInventoryResponseDto>>($"{InventoryUrl}?lowStockOnly=true");

        Assert.Contains(rows!, r => r.MedicineId == low.Id);
        Assert.DoesNotContain(rows!, r => r.MedicineId == healthy.Id);
        Assert.All(rows!, r => Assert.True(r.TotalQuantity <= InventoryApiFactory.LowStockThreshold));
    }

    // 5 — Scenario 1: stock update is reflected in availability
    [Fact]
    public async Task CreateBatch_Valid_Returns200AndMedicineBecomesAvailable()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Create-Valid");

        var response = await _staff.PostAsJsonAsync(BatchesUrl, Batch(medicine.Id, "CREATE-1", 50));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<StockBatchResponseDto>();
        Assert.True(created!.Id > 0);
        Assert.Equal(50, created.Quantity);

        var row = await InventoryRowAsync(medicine.Id);
        Assert.Equal(50, row.TotalQuantity);
        Assert.Equal("Available", row.AvailabilityStatus);
    }

    // 6
    [Fact]
    public async Task CreateBatch_NegativeQuantity_Returns400WithValidationMessage()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Negative-Qty");

        var response = await _staff.PostAsJsonAsync(BatchesUrl, Batch(medicine.Id, "NEG-1", -1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Stock quantity cannot be negative.", await MessageAsync(response));
    }

    // 7
    [Fact]
    public async Task CreateBatch_ExpiryBeforeManufacturing_Returns400WithValidationMessage()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Expiry-Order");
        var request = Batch(medicine.Id, "EXP-1", 10);
        request.ManufacturingDate = new DateTime(2027, 5, 1);
        request.ExpiryDate = new DateTime(2026, 5, 1);

        var response = await _staff.PostAsJsonAsync(BatchesUrl, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Expiry date cannot be before manufacturing date.", await MessageAsync(response));
    }

    // 8
    [Fact]
    public async Task CreateBatch_NonNumericQuantity_Returns400FromModelBinding()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-NonNumeric");
        var json = $$"""
            {"medicineId":{{medicine.Id}},"batchNumber":"NN-1","quantity":"ten","purchasePrice":1,"sellingPrice":2,
             "manufacturingDate":"2026-01-01","expiryDate":"2027-01-01"}
            """;

        var response = await _staff.PostAsync(BatchesUrl, new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("quantity", body, StringComparison.OrdinalIgnoreCase);
    }

    // 9
    [Fact]
    public async Task CreateBatch_UnknownMedicine_Returns404()
    {
        var response = await _staff.PostAsJsonAsync(BatchesUrl, Batch(999_999, "MISSING-1", 5));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Medicine was not found.", await MessageAsync(response));
    }

    // 10
    [Fact]
    public async Task CreateBatch_DuplicateBatchNumber_Returns409()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Duplicate");
        await _staff.PostAsJsonAsync(BatchesUrl, Batch(medicine.Id, "DUP-1", 5));

        var response = await _staff.PostAsJsonAsync(BatchesUrl, Batch(medicine.Id, "DUP-1", 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("This batch number already exists for the medicine.", await MessageAsync(response));
    }

    // 11 — Scenario 2: low stock shows up in the alert list and queues a LowStockDetected event
    [Fact]
    public async Task CreateBatch_BelowThreshold_AppearsInLowStockListAndQueuesEvent()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-LowStock-Alert");

        await _staff.PostAsJsonAsync(BatchesUrl, Batch(medicine.Id, "LOW-1", 4));

        var row = await InventoryRowAsync(medicine.Id, lowStockOnly: true);
        Assert.True(row.IsLowStock);
        Assert.Equal("Low stock", row.AvailabilityStatus);

        var eventTypes = await _factory.QueryAsync(db => db.EventOutboxEntries
            .Where(e => e.EventKey == medicine.Id.ToString())
            .Select(e => e.EventType)
            .ToListAsync());
        Assert.Contains(EventTypes.InventoryUpdated, eventTypes);
        Assert.Contains(EventTypes.LowStockDetected, eventTypes);
    }

    // 12 — Scenario 3: stock at zero marks the medicine out of stock so it cannot be ordered
    [Fact]
    public async Task UpdateBatch_ToZero_MarksMedicineOutOfStock()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-OutOfStock");
        var request = Batch(medicine.Id, "ZERO-1", 6);
        var created = await (await _staff.PostAsJsonAsync(BatchesUrl, request)).Content.ReadFromJsonAsync<StockBatchResponseDto>();
        request.Quantity = 0;

        var response = await _staff.PutAsJsonAsync($"{BatchesUrl}/{created!.Id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await InventoryRowAsync(medicine.Id);
        Assert.Equal(0, row.TotalQuantity);
        Assert.Equal("Out of stock", row.AvailabilityStatus);
        var stockQuantity = await _factory.QueryAsync(db => db.Medicines.Where(m => m.Id == medicine.Id).Select(m => m.StockQuantity).SingleAsync());
        Assert.Equal(0, stockQuantity);
        var hasOutOfStockEvent = await _factory.QueryAsync(db => db.EventOutboxEntries
            .AnyAsync(e => e.EventKey == medicine.Id.ToString() && e.EventType == EventTypes.MedicineOutOfStock));
        Assert.True(hasOutOfStockEvent);
    }

    // 13
    [Fact]
    public async Task UpdateBatch_UnknownId_Returns404()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Update-Missing");

        var response = await _staff.PutAsJsonAsync($"{BatchesUrl}/987654", Batch(medicine.Id, "X-1", 5));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Stock batch was not found.", await MessageAsync(response));
    }

    // 14
    [Fact]
    public async Task UpdateBatch_ChangingMedicine_Returns400()
    {
        var first = await _factory.SeedMedicineAsync("IT-Move-From");
        var second = await _factory.SeedMedicineAsync("IT-Move-To");
        var created = await (await _staff.PostAsJsonAsync(BatchesUrl, Batch(first.Id, "MOVE-1", 5))).Content.ReadFromJsonAsync<StockBatchResponseDto>();

        var response = await _staff.PutAsJsonAsync($"{BatchesUrl}/{created!.Id}", Batch(second.Id, "MOVE-1", 5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A stock batch cannot be moved to another medicine.", await MessageAsync(response));
    }

    // 15
    [Fact]
    public async Task GetBatches_ReturnsBatchesForMedicineOrderedByExpiry()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Batches");
        var later = Batch(medicine.Id, "LATER", 5); later.ExpiryDate = new DateTime(2028, 1, 1);
        var sooner = Batch(medicine.Id, "SOONER", 5); sooner.ExpiryDate = new DateTime(2026, 8, 1);
        await _staff.PostAsJsonAsync(BatchesUrl, later);
        await _staff.PostAsJsonAsync(BatchesUrl, sooner);

        var batches = await _staff.GetFromJsonAsync<List<StockBatchResponseDto>>($"{InventoryUrl}/{medicine.Id}/batches");

        Assert.Equal(new[] { "SOONER", "LATER" }, batches!.Select(b => b.BatchNumber).ToArray());
        Assert.All(batches!, b => Assert.Equal(medicine.Id, b.MedicineId));
        var row = await InventoryRowAsync(medicine.Id);
        Assert.Equal(10, row.TotalQuantity);
    }
}
