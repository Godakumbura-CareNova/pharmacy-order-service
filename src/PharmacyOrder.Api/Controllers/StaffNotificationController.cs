using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Security;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/staff/notifications")]
[Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]
public sealed class StaffNotificationController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StaffNotificationDto>>> List(CancellationToken ct)
    {
        return Ok(await db.StaffNotifications.AsNoTracking().OrderByDescending(item => item.CreatedAtUtc).Take(30)
            .Select(item => new StaffNotificationDto
            {
                Id = item.Id, Title = item.Title, Message = item.Message, RelatedEntityType = item.RelatedEntityType,
                RelatedEntityId = item.RelatedEntityId, CreatedAtUtc = item.CreatedAtUtc, IsRead = item.IsRead
            }).ToListAsync(ct));
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        var notification = await db.StaffNotifications.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (notification is null) return NotFound();
        notification.IsRead = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await db.StaffNotifications.Where(item => !item.IsRead)
            .ExecuteUpdateAsync(setter => setter.SetProperty(item => item.IsRead, true), ct);
        return NoContent();
    }
}
