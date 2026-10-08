using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Services.Interfaces;
namespace PharmacyOrder.Api.Controllers;
[ApiController, AllowAnonymous, Route("api/public/medicines")]
public class PublicMedicinesController(ICatalogueService catalogue) : ControllerBase
{
    private static object PublicFields(CatalogueMedicineResponseDto m) => new { m.Id, m.Name, m.Description, m.CategoryName, m.Price, m.ImagePath, m.PrescriptionRequired, m.AvailabilityStatus };
    [HttpGet]
    public async Task<IActionResult> List(string? search, int page = 1, int pageSize = 12)
    {
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 12 || search?.Length > 200) return BadRequest(new { message = "Please check your search criteria." });
        var result = await catalogue.GetMedicinesAsync(search, null, null, page, pageSize);
        return Ok(new { items = result.Items.Select(PublicFields), result.Page, result.PageSize, result.TotalCount, result.TotalPages });
    }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        try { return Ok(PublicFields(await catalogue.GetMedicineAsync(id))); }
        catch (KeyNotFoundException) { return NotFound(new { message = "This medicine is unavailable." }); }
    }
}
