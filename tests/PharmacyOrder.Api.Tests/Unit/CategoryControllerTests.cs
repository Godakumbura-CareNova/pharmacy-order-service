using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Category;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class CategoryControllerTests
{
    private readonly Mock<ICategoryService> _categoryService = new();
    private CategoryController CreateController() => new(_categoryService.Object);

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenServiceSucceeds()
    {
        var controller = CreateController();
        var response = new CategoryResponseDto { Id = 1, Name = "Vitamins" };
        _categoryService.Setup(s => s.CreateAsync(It.IsAny<CategoryRequestDto>())).ReturnsAsync(response);

        var result = await controller.Create(new CategoryRequestDto { Name = "Vitamins" });

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Theory]
    [InlineData("notfound")]
    [InlineData("badrequest")]
    [InlineData("conflict")]
    [InlineData("success")]
    public async Task Update_ReturnsExpectedResultForEachOutcome(string scenario)
    {
        var controller = CreateController();
        var request = new CategoryRequestDto { Name = "Vitamins" };

        switch (scenario)
        {
            case "notfound":
                _categoryService.Setup(s => s.UpdateAsync(1, request)).ThrowsAsync(new KeyNotFoundException());
                Assert.IsType<NotFoundObjectResult>((await controller.Update(1, request)).Result);
                break;
            case "badrequest":
                _categoryService.Setup(s => s.UpdateAsync(1, request)).ThrowsAsync(new ArgumentException());
                Assert.IsType<BadRequestObjectResult>((await controller.Update(1, request)).Result);
                break;
            case "conflict":
                _categoryService.Setup(s => s.UpdateAsync(1, request)).ThrowsAsync(new InvalidOperationException());
                Assert.IsType<ConflictObjectResult>((await controller.Update(1, request)).Result);
                break;
            case "success":
                _categoryService.Setup(s => s.UpdateAsync(1, request)).ReturnsAsync(new CategoryResponseDto { Id = 1, Name = "Vitamins" });
                Assert.IsType<OkObjectResult>((await controller.Update(1, request)).Result);
                break;
        }
    }

    [Theory]
    [InlineData("notfound")]
    [InlineData("conflict")]
    [InlineData("success")]
    public async Task Delete_ReturnsExpectedResultForEachOutcome(string scenario)
    {
        var controller = CreateController();

        switch (scenario)
        {
            case "notfound":
                _categoryService.Setup(s => s.DeleteAsync(1)).ThrowsAsync(new KeyNotFoundException());
                Assert.IsType<NotFoundObjectResult>(await controller.Delete(1));
                break;
            case "conflict":
                _categoryService.Setup(s => s.DeleteAsync(1)).ThrowsAsync(new InvalidOperationException());
                Assert.IsType<ConflictObjectResult>(await controller.Delete(1));
                break;
            case "success":
                _categoryService.Setup(s => s.DeleteAsync(1)).Returns(Task.CompletedTask);
                Assert.IsType<NoContentResult>(await controller.Delete(1));
                break;
        }
    }
}