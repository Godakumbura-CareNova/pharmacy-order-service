using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class CatalogueControllerTests
{
    private readonly Mock<ICatalogueService> _catalogue = new(MockBehavior.Strict);
    private CatalogueController CreateController() => new(_catalogue.Object);

    // P4 — GET /api/catalogue/medicines/{id} returns 200 with the medicine details
    [Fact]
    public async Task GetMedicine_ExistingMedicine_ReturnsOkWithDetails()
    {
        var dto = new CatalogueMedicineResponseDto { Id = 42, Name = "Amoxicillin 500mg", StockQuantity = 7, Price = 450.75m, PrescriptionRequired = true };
        _catalogue.Setup(s => s.GetMedicineAsync(42)).ReturnsAsync(dto);

        var result = await CreateController().GetMedicine(42);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(dto, ok.Value);
        _catalogue.VerifyAll();
    }

    // N3 — unavailable medicine → 404 with the service's message so the UI can show it
    [Fact]
    public async Task GetMedicine_Unavailable_ReturnsNotFoundWithMessage()
    {
        _catalogue.Setup(s => s.GetMedicineAsync(999)).ThrowsAsync(new KeyNotFoundException("Medicine is unavailable."));

        var result = await CreateController().GetMedicine(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Medicine is unavailable.", notFound.Value!.GetType().GetProperty("message")!.GetValue(notFound.Value));
    }
}
