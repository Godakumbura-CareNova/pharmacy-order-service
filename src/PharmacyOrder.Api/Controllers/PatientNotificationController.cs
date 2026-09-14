using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Patient;
using PharmacyOrder.Api.Security;

namespace PharmacyOrder.Api.Controllers;

[ApiController]
[Route("api/patient-notifications")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public sealed class PatientNotificationController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PatientNotificationDto>>> List(CancellationToken ct)
    {
        var patientId = GetPatientId();
        return Ok(await db.PatientNotifications.AsNoTracking().Where(item => item.PatientId == patientId)
            .OrderByDescending(item => item.CreatedAtUtc).Take(30).Select(item => new PatientNotificationDto
            {
                Id = item.Id, Title = item.Title, Message = item.Message, RelatedEntityType = item.RelatedEntityType,
                RelatedEntityId = item.RelatedEntityId, CreatedAtUtc = item.CreatedAtUtc, IsRead = item.IsRead
            }).ToListAsync(ct));
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        var notification = await db.PatientNotifications.SingleOrDefaultAsync(item => item.Id == id && item.PatientId == GetPatientId(), ct);
        if (notification is null) return NotFound();
        notification.IsRead = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var patientId = GetPatientId();
        await db.PatientNotifications.Where(item => item.PatientId == patientId && !item.IsRead)
            .ExecuteUpdateAsync(setter => setter.SetProperty(item => item.IsRead, true), ct);
        return NoContent();
    }

    private Guid GetPatientId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
