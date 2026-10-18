using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Category;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.UnitMocking;

// GCN-27 Medicine Category Management
public class GCN27_MedicineCategoryManagementTests
{
    // created category returns 201
    [Fact]
    public async Task Create_ValidCategory_Returns201_AndCallsServiceOnce()
    {
        var service = new Mock<ICategoryService>();
        service.Setup(s => s.CreateAsync(It.IsAny<CategoryRequestDto>()))
            .ReturnsAsync(new CategoryResponseDto { Id = 3, Name = "Vitamins" });

        var result = await new CategoryController(service.Object).Create(new CategoryRequestDto { Name = "Vitamins" });

        Assert.IsType<CreatedAtActionResult>(result.Result);
        service.Verify(s => s.CreateAsync(It.Is<CategoryRequestDto>(r => r.Name == "Vitamins")), Times.Once);
    }

    // service errors are turned into the right status code
    [Theory]
    [InlineData(typeof(ArgumentException), typeof(BadRequestObjectResult))]
    [InlineData(typeof(InvalidOperationException), typeof(ConflictObjectResult))]
    public async Task Create_ServiceError_ReturnsMatchingStatus(Type exceptionType, Type expectedResult)
    {
        var service = new Mock<ICategoryService>();
        service.Setup(s => s.CreateAsync(It.IsAny<CategoryRequestDto>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exceptionType, "error")!);

        var result = await new CategoryController(service.Object).Create(new CategoryRequestDto { Name = "Vitamins" });

        Assert.IsType(expectedResult, result.Result);
    }

    // name must be 2 to 80 characters
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public async Task CreateAsync_InvalidName_IsRejected(string name)
    {
        var db = TestDoubles.FakeDb();

        await Assert.ThrowsAsync<ArgumentException>(() => new CategoryService(db).CreateAsync(new CategoryRequestDto { Name = name }));

        Assert.Empty(db.Categories);
    }

    // duplicate name is not allowed, ignoring case
    [Fact]
    public async Task CreateAsync_DuplicateName_IsRejected()
    {
        var db = TestDoubles.FakeDb();
        var service = new CategoryService(db);
        await service.CreateAsync(new CategoryRequestDto { Name = "Vitamins" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CategoryRequestDto { Name = " vitamins " }));

        Assert.Single(db.Categories);
        Assert.Single(db.EventOutboxEntries, entry => entry.EventType == EventTypes.CategoryCreated);
    }

    // category used by an active medicine cannot be deleted
    [Fact]
    public async Task DeleteAsync_CategoryWithActiveMedicine_IsBlocked()
    {
        var db = TestDoubles.FakeDb();
        db.Categories.Add(new Category { Id = 1, Name = "Pain Relief" });
        db.Medicines.Add(new Medicine { Id = 1, Name = "Panadol", CategoryId = 1, IsActive = true });
        db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CategoryService(db).DeleteAsync(1));

        Assert.Single(db.Categories);
    }
}
