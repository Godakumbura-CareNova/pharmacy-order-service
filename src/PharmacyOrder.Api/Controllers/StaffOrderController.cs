using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/staff/orders")]
[Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
public sealed class StaffOrderController(StaffOrderService orders) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<StaffOrderDto>> List(CancellationToken ct) => orders.ListActiveAsync(ct);

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<OrderResponseDto>> Update(int id, StaffOrderStatusRequest request, CancellationToken ct)
    {
        try { return Ok(await orders.UpdateStatusAsync(id, request, ct)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }
}
