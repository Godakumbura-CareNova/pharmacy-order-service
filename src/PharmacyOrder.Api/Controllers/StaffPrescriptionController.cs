using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/staff/prescriptions")]
[Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
public sealed class StaffPrescriptionController(StaffPrescriptionService prescriptions) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<StaffPrescriptionDto>> List(CancellationToken ct) => prescriptions.ListPendingAsync(ct);

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> File(int id, CancellationToken ct)
    {
        try
        {
            var result = await prescriptions.GetFileAsync(id, ct);
            var provider = new FileExtensionContentTypeProvider();
            var contentType = provider.TryGetContentType(result.OriginalFileName, out var value) ? value : "application/octet-stream";
            return PhysicalFile(result.PhysicalPath, contentType, result.OriginalFileName);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPut("{id:int}/decision")]
    public async Task<ActionResult<StaffPrescriptionDto>> Decide(int id, PrescriptionDecisionRequest request, CancellationToken ct)
    {
        try { return Ok(await prescriptions.DecideAsync(id, request, ct)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }
}
