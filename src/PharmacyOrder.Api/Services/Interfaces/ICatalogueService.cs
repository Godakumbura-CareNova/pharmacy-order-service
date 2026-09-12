using PharmacyOrder.Api.DTOs.Catalogue;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface ICatalogueService
{
    Task<PaginatedCatalogueResponseDto> GetMedicinesAsync(string? search, int? categoryId, int page, int pageSize);
    Task<IReadOnlyList<CatalogueCategoryResponseDto>> GetCategoriesAsync();
}
