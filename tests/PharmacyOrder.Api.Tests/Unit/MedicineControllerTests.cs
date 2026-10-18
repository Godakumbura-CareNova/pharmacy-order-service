using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Medicine;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class MedicineControllerTests
{
    private readonly Mock<IMedicineService> _medicineService = new();
    private MedicineController CreateController() => new(_medicineService.Object);

    [Theory]
    [InlineData("notfound")]
    [InlineData("badrequest")]
    [InlineData("conflict")]
    [InlineData("success")]
    public async Task Create_ReturnsExpectedResultForEachOutcome(string scenario)
    {
        var controller = CreateController();
        var request = new MedicineRequestDto { Name = "Paracetamol", Description = "Tablets", CategoryId = 1, Price = 10m };

        switch (scenario)
        {
            case "notfound":
                _medicineService.Setup(s => s.CreateAsync(request)).ThrowsAsync(new KeyNotFoundException());
                Assert.IsType<NotFoundObjectResult>((await controller.Create(request)).Result);
                break;
            case "badrequest":
                _medicineService.Setup(s => s.CreateAsync(request)).ThrowsAsync(new ArgumentException());
                Assert.IsType<BadRequestObjectResult>((await controller.Create(request)).Result);
                break;
            case "conflict":
                _medicineService.Setup(s => s.CreateAsync(request)).ThrowsAsync(new InvalidOperationException());
                Assert.IsType<ConflictObjectResult>((await controller.Create(request)).Result);
                break;
            case "success":
                _medicineService.Setup(s => s.CreateAsync(request)).ReturnsAsync(new MedicineResponseDto { Id = 1, Name = "Paracetamol" });
                Assert.IsType<CreatedAtActionResult>((await controller.Create(request)).Result);
                break;
        }
    }

    [Theory]
    [InlineData("notfound")]
    [InlineData("success")]
    public async Task Deactivate_ReturnsExpectedResultForEachOutcome(string scenario)
    {
        var controller = CreateController();

        if (scenario == "notfound")
        {
            _medicineService.Setup(s => s.DeactivateAsync(1)).ThrowsAsync(new KeyNotFoundException());
            Assert.IsType<NotFoundObjectResult>(await controller.Deactivate(1));
        }
        else
        {
            _medicineService.Setup(s => s.DeactivateAsync(1)).Returns(Task.CompletedTask);
            Assert.IsType<NoContentResult>(await controller.Deactivate(1));
        }
    }
}