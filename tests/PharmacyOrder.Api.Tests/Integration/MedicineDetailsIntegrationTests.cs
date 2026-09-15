using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Catalogue;

namespace PharmacyOrder.Api.Tests.Integration;

/// <summary>GCN-26: GET /api/catalogue/medicines/{id} through the real HTTP pipeline.</summary>
public class MedicineDetailsIntegrationTests : IClassFixture<CatalogueApiFactory>
{
    private const string MedicinesUrl = "/api/catalogue/medicines";

    private readonly CatalogueApiFactory _factory;
    private readonly HttpClient _patient;

    public MedicineDetailsIntegrationTests(CatalogueApiFactory factory)
    {
        _factory = factory;
        _patient = factory.CreatePatientClient();
    }

    private async Task<int> SeedMedicineIdAsync(string name, int stock, bool active = true, bool prescription = false)
    {
        var category = await _factory.SeedCategoryAsync($"IT-Cat-{name}", (name, 320.5m, stock, active));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = await db.Medicines.SingleAsync(m => m.Name == name);
        if (prescription)
        {
            medicine.PrescriptionRequired = true;
            await db.SaveChangesAsync();
        }
        return medicine.Id;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return body!["message"].ToString()!;
    }

    // 1 — no token → 401
    [Fact]
    public async Task GetMedicine_WithoutToken_Returns401()
    {
        var id = await SeedMedicineIdAsync("IT-Detail-Anon", stock: 5);

        var response = await _factory.CreateClientAs(role: null).GetAsync($"{MedicinesUrl}/{id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 2 — non-customer role → 403
    [Fact]
    public async Task GetMedicine_WithStaffRole_Returns403()
    {
        var id = await SeedMedicineIdAsync("IT-Detail-Staff", stock: 5);

        var response = await _factory.CreateClientAs("PharmacyStaff").GetAsync($"{MedicinesUrl}/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // 3 — detail view returns every field the story asks for
    [Fact]
    public async Task GetMedicine_AsPatient_ReturnsFullDetails()
    {
        var id = await SeedMedicineIdAsync("IT-Detail-Full", stock: 9, prescription: true);

        var medicine = await _patient.GetFromJsonAsync<CatalogueMedicineResponseDto>($"{MedicinesUrl}/{id}");

        Assert.Equal("IT-Detail-Full", medicine!.Name);
        Assert.Equal("IT-Detail-Full description", medicine.Description);
        Assert.Equal("IT-Cat-IT-Detail-Full", medicine.CategoryName);
        Assert.Equal(320.5m, medicine.Price);
        Assert.Equal(9, medicine.StockQuantity);
        Assert.True(medicine.PrescriptionRequired);
        Assert.Equal("Available", medicine.AvailabilityStatus);
    }

    // 4 — unknown id → 404 with a message the UI can display
    [Fact]
    public async Task GetMedicine_UnknownId_Returns404WithMessage()
    {
        var response = await _patient.GetAsync($"{MedicinesUrl}/987654");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Medicine is unavailable.", await MessageAsync(response));
    }

    // 5 — deactivated medicine cannot be viewed
    [Fact]
    public async Task GetMedicine_InactiveMedicine_Returns404()
    {
        var id = await SeedMedicineIdAsync("IT-Detail-Inactive", stock: 5, active: false);

        var response = await _patient.GetAsync($"{MedicinesUrl}/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // 6 — out-of-stock medicine is still viewable but flagged so the UI can block purchase
    //     (behaviour changed on dev in GCN-32: "allow viewing out of stock medicine details").
    [Fact]
    public async Task GetMedicine_OutOfStock_Returns200FlaggedOutOfStock()
    {
        var id = await SeedMedicineIdAsync("IT-Detail-NoStock", stock: 0);

        var response = await _patient.GetAsync($"{MedicinesUrl}/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"stockQuantity\":0", body);
        Assert.Contains("Out of stock", body);
    }

    // 7 — malformed id (non-numeric) does not match the route → 404, never a 500
    [Fact]
    public async Task GetMedicine_NonNumericId_Returns404()
    {
        var response = await _patient.GetAsync($"{MedicinesUrl}/abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // 8 — the id shown in the catalogue list opens the same medicine's details (list → detail navigation)
    [Fact]
    public async Task GetMedicine_IdFromCatalogueList_OpensMatchingDetails()
    {
        var id = await SeedMedicineIdAsync("IT-Detail-FromList", stock: 3);

        var list = await _patient.GetFromJsonAsync<PaginatedCatalogueResponseDto>($"{MedicinesUrl}?search=IT-Detail-FromList");
        var listed = Assert.Single(list!.Items);
        var details = await _patient.GetFromJsonAsync<CatalogueMedicineResponseDto>($"{MedicinesUrl}/{listed.Id}");

        Assert.Equal(id, listed.Id);
        Assert.Equal(listed.Name, details!.Name);
        Assert.Equal(listed.Price, details.Price);
        Assert.Equal(listed.StockQuantity, details.StockQuantity);
    }
}
