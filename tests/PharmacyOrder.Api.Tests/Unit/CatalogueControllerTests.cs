using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class CatalogueControllerTests
{
    [Fact]
    public async Task GetMedicines_ReturnsCataloguePageFromService()
    {
        var expected = new PaginatedCatalogueResponseDto { Page = 2, PageSize = 12, TotalCount = 13, TotalPages = 2 };
        var catalogue = new Mock<ICatalogueService>();
        catalogue.Setup(service => service.GetMedicinesAsync("pain", 3, true, 2, 12)).ReturnsAsync(expected);
        var controller = new CatalogueController(catalogue.Object);

        var result = await controller.GetMedicines("pain", 3, true, 2, 12);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task GetCategories_ReturnsCategoriesFromService()
    {
        var expected = new[] { new CatalogueCategoryResponseDto { Id = 1, Name = "Pain Relief" } };
        var catalogue = new Mock<ICatalogueService>();
        catalogue.Setup(service => service.GetCategoriesAsync()).ReturnsAsync(expected);
        var controller = new CatalogueController(catalogue.Object);

        var result = await controller.GetCategories();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }
}
