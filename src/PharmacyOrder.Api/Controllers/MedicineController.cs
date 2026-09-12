using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyOrder.Api.DTOs.Medicine;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/staff/medicines")]
[Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
public class MedicineController : ControllerBase
{
    private readonly IMedicineService _medicineService;
    public MedicineController(IMedicineService medicineService) => _medicineService = medicineService;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MedicineResponseDto>>> GetAll([FromQuery] bool includeInactive = true) => Ok(await _medicineService.GetAllAsync(includeInactive));

    [HttpPost]
    public async Task<ActionResult<MedicineResponseDto>> Create(MedicineRequestDto request)
    {
        try { var medicine = await _medicineService.CreateAsync(request); return CreatedAtAction(nameof(GetAll), new { id = medicine.Id }, medicine); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MedicineResponseDto>> Update(int id, MedicineRequestDto request)
    {
        try { return Ok(await _medicineService.UpdateAsync(id, request)); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpPatch("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        try { await _medicineService.DeactivateAsync(id); return NoContent(); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
    }
}
