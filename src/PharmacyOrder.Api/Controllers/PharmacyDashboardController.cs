using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
public sealed class PharmacyDashboardController(PharmacyDashboardService service) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet("api/admin/dashboard/operations")]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        try { return Ok(await service.GetAdminOperationsAsync(ct)); }
        catch (DbException) { return StatusCode(503, new { message = "Pharmacy information is unavailable. Please retry." }); }
    }

    [Authorize(Policy = AuthorizationPolicies.Patient)]
    [HttpGet("api/patient/dashboard/orders")]
    public async Task<IActionResult> Patient(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty) return Unauthorized();
        try { return Ok(await service.GetPatientOrdersAsync(id, ct)); }
        catch (DbException) { return StatusCode(503, new { message = "Order information is unavailable. Please retry." }); }
    }

    [Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
    [HttpGet("api/staff/dashboard/operations")]
    public async Task<IActionResult> Staff(CancellationToken ct)
    {
        try { return Ok(await service.GetStaffOperationsAsync(ct)); }
        catch (DbException) { return StatusCode(503, new { message = "Pharmacy information is unavailable. Please retry." }); }
    }
}
