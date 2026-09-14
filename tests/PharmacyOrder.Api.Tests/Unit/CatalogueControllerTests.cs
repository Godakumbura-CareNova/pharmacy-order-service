using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class CatalogueControllerTests
{
    // P7 — the controller forwards the customer's query parameters and returns the service data as 200 OK
    [Fact]
    public async Task Controller_ForwardsQueryParametersAndReturnsOkForBothEndpoints()
    {
        var page = new PaginatedCatalogueResponseDto { Page = 2, PageSize = 12, TotalCount = 13, TotalPages = 2 };
        var categories = new[] { new CatalogueCategoryResponseDto { Id = 1, Name = "Pain Relief" } };
        var catalogue = new Mock<ICatalogueService>(MockBehavior.Strict);
        catalogue.Setup(s => s.GetMedicinesAsync("pain", 3, 2, 12)).ReturnsAsync(page);
        catalogue.Setup(s => s.GetCategoriesAsync()).ReturnsAsync(categories);
        var controller = new CatalogueController(catalogue.Object);

        var medicinesResult = await controller.GetMedicines("pain", 3, 2, 12);
        var categoriesResult = await controller.GetCategories();

        Assert.Same(page, Assert.IsType<OkObjectResult>(medicinesResult.Result).Value);
        Assert.Same(categories, Assert.IsType<OkObjectResult>(categoriesResult.Result).Value);
        catalogue.VerifyAll();
    }
}
