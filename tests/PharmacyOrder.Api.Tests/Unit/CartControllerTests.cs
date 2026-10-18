using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

public class CartControllerTests
{
    [Fact]
    public async Task GetMine_ForwardsGuidPatientIdentifierToCartService()
    {
        var patientId = Guid.NewGuid();
        var cart = new Mock<ICartService>();
        cart.Setup(service => service.GetCartAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartResponseDto());
        var controller = new CartController(cart.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, patientId.ToString())], "Test"))
                }
            }
        };

        var result = await controller.GetMine(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
