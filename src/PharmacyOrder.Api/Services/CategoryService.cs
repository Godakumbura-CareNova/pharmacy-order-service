using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Category;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _dbContext;

    public CategoryService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<CategoryResponseDto>> GetAllAsync() =>
        await _dbContext.Categories
            .OrderBy(category => category.Name)
            .Select(category => new CategoryResponseDto
            {
                Id = category.Id,
                Name = category.Name,
                MedicineCount = category.Medicines.Count
            })
            .ToListAsync();

    public async Task<CategoryResponseDto> CreateAsync(CategoryRequestDto request)
    {
        var name = NormalizeName(request.Name);
        await EnsureNameAvailableAsync(name, null);
        var category = new Category { Name = name };
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();
        await AddEventAsync(EventTypes.CategoryCreated, category);
        return ToResponse(category, 0);
    }

    public async Task<CategoryResponseDto> UpdateAsync(int id, CategoryRequestDto request)
    {
        var category = await _dbContext.Categories.FindAsync(id)
            ?? throw new KeyNotFoundException("Category was not found.");
        var name = NormalizeName(request.Name);
        await EnsureNameAvailableAsync(name, id);
        category.Name = name;
        await _dbContext.SaveChangesAsync();
        await AddEventAsync(EventTypes.CategoryUpdated, category);
        return ToResponse(category, await _dbContext.Medicines.CountAsync(medicine => medicine.CategoryId == id));
    }

    public async Task DeleteAsync(int id)
    {
        var category = await _dbContext.Categories.FindAsync(id)
            ?? throw new KeyNotFoundException("Category was not found.");
        if (await _dbContext.Medicines.AnyAsync(medicine => medicine.CategoryId == id && medicine.IsActive))
            throw new InvalidOperationException("A category assigned to an active medicine cannot be deleted.");
        _dbContext.Categories.Remove(category);
        await _dbContext.SaveChangesAsync();
        await AddEventAsync(EventTypes.CategoryDeleted, category);
    }

    private async Task EnsureNameAvailableAsync(string name, int? excludedId)
    {
        var duplicate = await _dbContext.Categories.AnyAsync(category =>
            category.Name.ToLower() == name.ToLower() && (!excludedId.HasValue || category.Id != excludedId));
        if (duplicate) throw new InvalidOperationException("A category with this name already exists.");
    }

    private async Task AddEventAsync(string eventType, Category category)
    {
        var eventId = Guid.NewGuid();
        var occurredAtUtc = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new MedicineEvent(eventId, eventType, occurredAtUtc, category.Id, "Category", JsonSerializer.SerializeToElement(new { category.Name })));
        _dbContext.EventOutboxEntries.Add(new EventOutbox { EventId = eventId, Topic = EventTypes.MedicineTopic, EventType = eventType, EventKey = category.Id.ToString(), PayloadJson = payload, OccurredAtUtc = occurredAtUtc });
        await _dbContext.SaveChangesAsync();
    }

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 80) throw new ArgumentException("Category name must contain 2 to 80 characters.");
        return normalized;
    }

    private static CategoryResponseDto ToResponse(Category category, int medicineCount) => new() { Id = category.Id, Name = category.Name, MedicineCount = medicineCount };
}
