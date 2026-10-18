using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>GCN-30 unit test 7: the controller acts for the signed-in customer and maps outcomes to HTTP results.</summary>
public class CartControllerQaTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private readonly Mock<ICartService> _cart = new(MockBehavior.Strict);

    private CartController CreateController()
    {
        var controller = new CartController(_cart.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Customer.ToString()) }, "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return controller;
    }

    // 7 — GET/POST/PUT/DELETE return 200 with the cart; validation → 400 with message; unknown item → 404
    [Fact]
    public async Task Controller_UsesSignedInCustomerAndMapsServiceOutcomes()
    {
        var controller = CreateController();
        var cart = new CartResponseDto { CartTotal = 300m };
        var add = new AddCartItemRequestDto { MedicineId = 1, Quantity = 2 };
        var update = new UpdateCartItemRequestDto { Quantity = 3 };
        var tooMany = new UpdateCartItemRequestDto { Quantity = 99 };

        _cart.Setup(s => s.GetCartAsync(Customer, It.IsAny<CancellationToken>())).ReturnsAsync(cart);
        _cart.Setup(s => s.AddNonPrescriptionItemAsync(Customer, add, It.IsAny<CancellationToken>())).ReturnsAsync(cart);
        _cart.Setup(s => s.UpdateItemAsync(Customer, 5, update, It.IsAny<CancellationToken>())).ReturnsAsync(cart);
        _cart.Setup(s => s.UpdateItemAsync(Customer, 5, tooMany, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Quantity must be between 1 and 10."));
        _cart.Setup(s => s.RemoveItemAsync(Customer, 5, It.IsAny<CancellationToken>())).ReturnsAsync(cart);
        _cart.Setup(s => s.RemoveItemAsync(Customer, 404, It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException("Cart item was not found."));

        Assert.Same(cart, Assert.IsType<OkObjectResult>((await controller.GetMine(CancellationToken.None)).Result).Value);
        Assert.Same(cart, Assert.IsType<OkObjectResult>((await controller.AddItem(add, CancellationToken.None)).Result).Value);
        Assert.Same(cart, Assert.IsType<OkObjectResult>((await controller.UpdateItem(5, update, CancellationToken.None)).Result).Value);
        Assert.Same(cart, Assert.IsType<OkObjectResult>((await controller.RemoveItem(5, CancellationToken.None)).Result).Value);

        var badRequest = Assert.IsType<BadRequestObjectResult>((await controller.UpdateItem(5, tooMany, CancellationToken.None)).Result);
        Assert.Equal("Quantity must be between 1 and 10.", badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value));
        Assert.IsType<NotFoundObjectResult>((await controller.RemoveItem(404, CancellationToken.None)).Result);
        _cart.VerifyAll();
    }
}
