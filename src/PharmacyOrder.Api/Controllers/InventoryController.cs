using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.DTOs.Inventory;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/staff/inventory")]
[Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventory;
    public InventoryController(IInventoryService inventory) => _inventory = inventory;
    [HttpGet] public async Task<ActionResult<IReadOnlyList<MedicineInventoryResponseDto>>> Get([FromQuery] bool lowStockOnly = false) => Ok(await _inventory.GetInventoryAsync(lowStockOnly));
    [HttpGet("{medicineId:int}/batches")] public async Task<ActionResult<IReadOnlyList<StockBatchResponseDto>>> Batches(int medicineId) => Ok(await _inventory.GetBatchesAsync(medicineId));
    [HttpPost("batches")] public async Task<ActionResult<StockBatchResponseDto>> Create(StockBatchRequestDto request) => await Save(() => _inventory.CreateBatchAsync(request));
    [HttpPut("batches/{id:int}")] public async Task<ActionResult<StockBatchResponseDto>> Update(int id, StockBatchRequestDto request) => await Save(() => _inventory.UpdateBatchAsync(id, request));
    private async Task<ActionResult<StockBatchResponseDto>> Save(Func<Task<StockBatchResponseDto>> action)
    { try { return Ok(await action()); } catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Stock changed while you were editing. Reload inventory and try again." }); } catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); } catch (ArgumentException e) { return BadRequest(new { message = e.Message }); } catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); } }
}
