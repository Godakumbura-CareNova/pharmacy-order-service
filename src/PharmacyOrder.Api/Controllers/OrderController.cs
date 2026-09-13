using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class OrderController : ControllerBase
{
    private readonly OrderService _orders;
    private readonly ILogger<OrderController> _logger;
    public OrderController(OrderService orders, ILogger<OrderController> logger) { _orders = orders; _logger = logger; }

    [HttpGet("review")]
    public Task<ActionResult<CheckoutReviewDto>> Review(CancellationToken ct) =>
        Execute(id => _orders.ReviewAsync(id, ct));

    [HttpPost]
    public Task<ActionResult<OrderResponseDto>> Place(OrderRequestDto request, CancellationToken ct) =>
        Execute(id => _orders.PlaceAsync(id, request, ct));

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<OrderResponseDto>>> List(CancellationToken ct) =>
        Execute(id => _orders.ListAsync(id, ct));

    [HttpGet("{id:int}")]
    public Task<ActionResult<OrderResponseDto>> Get(int id, CancellationToken ct) =>
        Execute(patientId => _orders.GetAsync(patientId, id, ct));

    private async Task<ActionResult<T>> Execute<T>(Func<Guid, Task<T>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var patientId) || patientId == Guid.Empty)
            return Unauthorized(new { message = "Please sign in again." });
        try { return Ok(await action(patientId)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Stock or cart details changed. Review your cart and try again." }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            _logger.LogError(exception, "Order database operation failed for patient {PatientId}", patientId);
            return StatusCode(503, new { message = "Unable to complete this request. Please retry; your order will not be duplicated." });
        }
    }
}
