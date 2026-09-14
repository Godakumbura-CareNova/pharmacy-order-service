using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Medicine;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class MedicineService : IMedicineService
{
    private readonly AppDbContext _dbContext;
    public MedicineService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<MedicineResponseDto>> GetAllAsync(bool includeInactive)
    {
        var query = _dbContext.Medicines.Include(medicine => medicine.Category).AsQueryable();
        if (!includeInactive) query = query.Where(medicine => medicine.IsActive);
        return await query.OrderBy(medicine => medicine.Name).Select(medicine => ToResponse(medicine)).ToListAsync();
    }

    public async Task<MedicineResponseDto> CreateAsync(MedicineRequestDto request)
    {
        await ValidateAsync(request, null);
        var medicine = new Medicine { Name = request.Name.Trim(), Description = request.Description.Trim(), CategoryId = request.CategoryId, Price = request.Price, PrescriptionRequired = request.PrescriptionRequired };
        _dbContext.Medicines.Add(medicine);
        await _dbContext.SaveChangesAsync();
        await AddEventAsync(EventTypes.MedicineCreated, medicine);
        medicine.Category = await _dbContext.Categories.FindAsync(medicine.CategoryId);
        return ToResponse(medicine);
    }

    public async Task<MedicineResponseDto> UpdateAsync(int id, MedicineRequestDto request)
    {
        var medicine = await _dbContext.Medicines.Include(item => item.Category).SingleOrDefaultAsync(item => item.Id == id) ?? throw new KeyNotFoundException("Medicine was not found.");
        await ValidateAsync(request, id);
        medicine.Name = request.Name.Trim(); medicine.Description = request.Description.Trim(); medicine.CategoryId = request.CategoryId; medicine.Price = request.Price; medicine.PrescriptionRequired = request.PrescriptionRequired;
        await _dbContext.SaveChangesAsync(); await AddEventAsync(EventTypes.MedicineUpdated, medicine);
        medicine.Category = await _dbContext.Categories.FindAsync(medicine.CategoryId);
        return ToResponse(medicine);
    }

    public async Task DeactivateAsync(int id)
    {
        var medicine = await _dbContext.Medicines.FindAsync(id) ?? throw new KeyNotFoundException("Medicine was not found.");
        medicine.IsActive = false; await _dbContext.SaveChangesAsync(); await AddEventAsync(EventTypes.MedicineDeactivated, medicine);
    }

    public async Task ActivateAsync(int id)
    {
        var medicine = await _dbContext.Medicines.FindAsync(id) ?? throw new KeyNotFoundException("Medicine was not found.");
        medicine.IsActive = true; await _dbContext.SaveChangesAsync(); await AddEventAsync(EventTypes.MedicineActivated, medicine);
    }

    public async Task<MedicineResponseDto> UpdateImageAsync(int id, string imagePath)
    {
        var medicine = await _dbContext.Medicines.Include(item => item.Category).SingleOrDefaultAsync(item => item.Id == id) ?? throw new KeyNotFoundException("Medicine was not found.");
        medicine.ImagePath = imagePath; await _dbContext.SaveChangesAsync(); await AddEventAsync(EventTypes.MedicineUpdated, medicine);
        return ToResponse(medicine);
    }

    private async Task ValidateAsync(MedicineRequestDto request, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120) throw new ArgumentException("Medicine name is required and must be 120 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 2000) throw new ArgumentException("Medicine description is required and must be 2000 characters or fewer.");
        if (request.Price < 0) throw new ArgumentException("Medicine price cannot be negative.");
        if (!await _dbContext.Categories.AnyAsync(category => category.Id == request.CategoryId)) throw new KeyNotFoundException("Category was not found.");
        var name = request.Name.Trim().ToLower();
        if (await _dbContext.Medicines.AnyAsync(medicine => medicine.IsActive && medicine.CategoryId == request.CategoryId && medicine.Name.ToLower() == name && (!excludeId.HasValue || medicine.Id != excludeId))) throw new InvalidOperationException("An active medicine with this name already exists in the category.");
    }

    private async Task AddEventAsync(string eventType, Medicine medicine)
    {
        var eventId = Guid.NewGuid(); var occurredAtUtc = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new MedicineEvent(eventId, eventType, occurredAtUtc, medicine.Id, "Medicine", JsonSerializer.SerializeToElement(new { medicine.Name, medicine.CategoryId, medicine.Price, medicine.PrescriptionRequired, medicine.IsActive })));
        _dbContext.EventOutboxEntries.Add(new EventOutbox { EventId = eventId, Topic = EventTypes.MedicineTopic, EventType = eventType, EventKey = medicine.Id.ToString(), PayloadJson = payload, OccurredAtUtc = occurredAtUtc });
        await _dbContext.SaveChangesAsync();
    }

    private static MedicineResponseDto ToResponse(Medicine medicine) => new() { Id = medicine.Id, Name = medicine.Name, Description = medicine.Description, CategoryId = medicine.CategoryId, CategoryName = medicine.Category?.Name ?? string.Empty, Price = medicine.Price, ImagePath = medicine.ImagePath, PrescriptionRequired = medicine.PrescriptionRequired, IsActive = medicine.IsActive };
}
