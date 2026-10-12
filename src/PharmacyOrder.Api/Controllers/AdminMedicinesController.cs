using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/admin/medicines")]
[Authorize(Roles = "Admin")]
public sealed class AdminMedicinesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100 || search?.Length > 100)
            return BadRequest(new { message = "Check the search and page values." });
        var query = db.Medicines.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.Name.Contains(term) || (m.Category != null && m.Category.Name.Contains(term)));
        }
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(m => m.Name).ThenBy(m => m.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new { m.Id, m.Name, CategoryName = m.Category == null ? "" : m.Category.Name,
                m.Price, m.StockQuantity, m.IsActive, m.PrescriptionRequired }).ToArrayAsync(ct);
        return Ok(new { items, totalCount, page, pageSize, totalPages = (int)Math.Ceiling((double)totalCount / pageSize) });
    }
}
