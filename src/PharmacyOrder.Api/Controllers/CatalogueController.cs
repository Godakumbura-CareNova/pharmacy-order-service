using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/catalogue")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class CatalogueController : ControllerBase
{
    private readonly ICatalogueService _catalogue;

    public CatalogueController(ICatalogueService catalogue) => _catalogue = catalogue;

    [HttpGet("medicines")]
    public async Task<ActionResult<PaginatedCatalogueResponseDto>> GetMedicines(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] bool? inStock,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        return Ok(await _catalogue.GetMedicinesAsync(search, categoryId, inStock, page, pageSize));
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CatalogueCategoryResponseDto>>> GetCategories()
    {
        return Ok(await _catalogue.GetCategoriesAsync());
    }

    [HttpGet("medicines/{id:int}")]
    public async Task<ActionResult<CatalogueMedicineResponseDto>> GetMedicine(int id)
    {
        try { return Ok(await _catalogue.GetMedicineAsync(id)); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
    }
}
