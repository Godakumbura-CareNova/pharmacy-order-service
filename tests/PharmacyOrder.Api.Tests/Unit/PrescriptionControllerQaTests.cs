using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Prescription;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>GCN-29 unit test 8: the controller maps service outcomes to HTTP results for the signed-in customer.</summary>
public class PrescriptionControllerQaTests
{
    private static readonly Guid Patient = Guid.NewGuid();
    private readonly Mock<IPrescriptionService> _prescriptions = new(MockBehavior.Strict);

    private PrescriptionController CreateController()
    {
        var controller = new PrescriptionController(_prescriptions.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Patient.ToString()) }, "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return controller;
    }

    // 8 — 201 Created on success (with the caller's id), 400 for validation failures, 404 for unavailable medicines
    [Fact]
    public async Task Upload_MapsServiceOutcomesToHttpResultsForSignedInCustomer()
    {
        var controller = CreateController();
        var ok = new PrescriptionUploadRequestDto { MedicineId = 1, Quantity = 2 };
        var noFile = new PrescriptionUploadRequestDto { MedicineId = 1, Quantity = 2 };
        var missing = new PrescriptionUploadRequestDto { MedicineId = 999, Quantity = 1 };
        var created = new PrescriptionResponseDto { Id = 5, MedicineId = 1, MedicineName = "Amoxicillin 500mg", Quantity = 2, Status = "PendingVerification" };

        _prescriptions.Setup(s => s.UploadAsync(Patient, ok, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        _prescriptions.Setup(s => s.UploadAsync(Patient, noFile, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("A prescription file is required."));
        _prescriptions.Setup(s => s.UploadAsync(Patient, missing, It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException("Prescription medicine is unavailable."));
        _prescriptions.Setup(s => s.GetForPatientAsync(Patient)).ReturnsAsync(new[] { created });

        var createdResult = Assert.IsType<CreatedAtActionResult>((await controller.Upload(ok, CancellationToken.None)).Result);
        Assert.Same(created, createdResult.Value);
        Assert.Equal(nameof(PrescriptionController.DownloadFile), createdResult.ActionName);
        Assert.Equal(5, createdResult.RouteValues!["id"]);

        var badRequest = Assert.IsType<BadRequestObjectResult>((await controller.Upload(noFile, CancellationToken.None)).Result);
        Assert.Equal("A prescription file is required.", badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value));

        Assert.IsType<NotFoundObjectResult>((await controller.Upload(missing, CancellationToken.None)).Result);

        var mine = Assert.IsType<OkObjectResult>((await controller.GetMine()).Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<PrescriptionResponseDto>>(mine.Value));
        _prescriptions.VerifyAll();
    }
}
