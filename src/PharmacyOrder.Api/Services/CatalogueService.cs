using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Catalogue;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class CatalogueService : ICatalogueService
{
    private readonly AppDbContext _db;

    public CatalogueService(AppDbContext db) => _db = db;

    public async Task<PaginatedCatalogueResponseDto> GetMedicinesAsync(string? search, int? categoryId, int page, int pageSize)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 12);
        var query = _db.Medicines
            .Include(medicine => medicine.Category)
            .Where(medicine => medicine.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(medicine => medicine.Name.ToLower().Contains(term) || medicine.Description.ToLower().Contains(term));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(medicine => medicine.CategoryId == categoryId.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(medicine => medicine.Name)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(medicine => new CatalogueMedicineResponseDto
            {
                Id = medicine.Id,
                Name = medicine.Name,
                Description = medicine.Description,
                CategoryId = medicine.CategoryId,
                CategoryName = medicine.Category!.Name,
                Price = medicine.Price,
                ImagePath = medicine.ImagePath,
                PrescriptionRequired = medicine.PrescriptionRequired,
                StockQuantity = medicine.StockQuantity,
                AvailabilityStatus = medicine.StockQuantity == 0 ? "Out of stock" : "Available"
            })
            .ToListAsync();

        return new PaginatedCatalogueResponseDto
        {
            Items = items,
            Page = safePage,
            PageSize = safePageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)safePageSize)
        };
    }

    public async Task<IReadOnlyList<CatalogueCategoryResponseDto>> GetCategoriesAsync()
    {
        return await _db.Categories
            .Where(category => category.Medicines.Any(medicine => medicine.IsActive))
            .OrderBy(category => category.Name)
            .Select(category => new CatalogueCategoryResponseDto { Id = category.Id, Name = category.Name })
            .ToListAsync();
    }
}
