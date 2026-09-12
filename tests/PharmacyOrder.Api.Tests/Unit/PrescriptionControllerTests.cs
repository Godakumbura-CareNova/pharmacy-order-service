using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Prescription;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class PrescriptionControllerTests
{
    [Fact]
    public async Task Upload_AcceptsGuidPatientIdentifierFromAuthenticationToken()
    {
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions
            .Setup(service => service.UploadAsync(It.IsAny<Guid>(), It.IsAny<PrescriptionUploadRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrescriptionResponseDto { Id = 1 });

        var controller = new PrescriptionController(prescriptions.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"))
                }
            }
        };

        var result = await controller.Upload(new PrescriptionUploadRequestDto(), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }
}
