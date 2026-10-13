using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PharmacyOrder.Api.Controllers;
[ApiController]
[Route("api/admin/reports/medicine-costs")]
[Authorize(Roles = "Admin")]
public sealed class AdminMedicineCostsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int[] orderIds, CancellationToken ct = default)
    {
        if (orderIds.Length is < 1 or > 100 || orderIds.Any(id => id < 1) || orderIds.Distinct().Count() != orderIds.Length)
            return BadRequest(new { message = "Provide between 1 and 100 distinct positive order IDs." });
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Include(o => o.Items)
            .ThenInclude(i => i.Allocations).ThenInclude(a => a.Stock).OrderBy(o => o.Id).ToArrayAsync(ct);
        var rows = orders.Select(o => new { OrderId = o.Id, o.CustomerId, o.TotalAmount, BuyingCost = Cost(o) });
        return Ok(rows);
    }
    private static decimal? Cost(Order order)
    {
        if (order.Status == OrderStatus.Rejected || order.Items.Count == 0 || order.Items.Any(i => i.Quantity <= 0 ||
            i.Allocations.Sum(a => (long)a.Quantity) != i.Quantity || i.Allocations.Any(a => a.Quantity <= 0 || a.Stock == null ||
                a.Stock.MedicineId != i.MedicineId || a.Stock.PurchasePrice < 0))) return null;
        return order.Items.Sum(i => i.Allocations.Sum(a => a.Quantity * a.Stock!.PurchasePrice));
    }
}
