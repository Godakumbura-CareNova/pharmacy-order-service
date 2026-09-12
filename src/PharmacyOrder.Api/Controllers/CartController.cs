using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class CartController : ControllerBase
{
    private readonly ICartService _cart;

    public CartController(ICartService cart) => _cart = cart;

    [HttpGet]
    public async Task<ActionResult<CartResponseDto>> GetMine(CancellationToken cancellationToken) =>
        Ok(await _cart.GetCartAsync(GetPatientId(), cancellationToken));

    [HttpPost("items")]
    public Task<ActionResult<CartResponseDto>> AddItem(AddCartItemRequestDto request, CancellationToken cancellationToken) =>
        Execute(() => _cart.AddNonPrescriptionItemAsync(GetPatientId(), request, cancellationToken));

    [HttpPut("items/{id:int}")]
    public Task<ActionResult<CartResponseDto>> UpdateItem(int id, UpdateCartItemRequestDto request, CancellationToken cancellationToken) =>
        Execute(() => _cart.UpdateItemAsync(GetPatientId(), id, request, cancellationToken));

    [HttpDelete("items/{id:int}")]
    public Task<ActionResult<CartResponseDto>> RemoveItem(int id, CancellationToken cancellationToken) =>
        Execute(() => _cart.RemoveItemAsync(GetPatientId(), id, cancellationToken));

    private async Task<ActionResult<CartResponseDto>> Execute(Func<Task<CartResponseDto>> action)
    {
        try { return Ok(await action()); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
    }

    private Guid GetPatientId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
