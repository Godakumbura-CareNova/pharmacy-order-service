using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using PharmacyOrder.Api.DTOs.Prescription;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/prescriptions")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class PrescriptionController : ControllerBase
{
    private readonly IPrescriptionService _prescriptions;

    public PrescriptionController(IPrescriptionService prescriptions) => _prescriptions = prescriptions;

    [HttpPost]
    public async Task<ActionResult<PrescriptionResponseDto>> Upload([FromForm] PrescriptionUploadRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _prescriptions.UploadAsync(GetPatientId(), request, cancellationToken);
            return CreatedAtAction(nameof(DownloadFile), new { id = result.Id }, result);
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PrescriptionResponseDto>>> GetMine()
    {
        return Ok(await _prescriptions.GetForPatientAsync(GetPatientId()));
    }

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> DownloadFile(int id)
    {
        try
        {
            var result = await _prescriptions.GetOwnedFileAsync(GetPatientId(), id);
            var provider = new FileExtensionContentTypeProvider();
            var contentType = provider.TryGetContentType(result.OriginalFileName, out var value) ? value : "application/octet-stream";
            return PhysicalFile(result.PhysicalPath, contentType, result.OriginalFileName);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private Guid GetPatientId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
