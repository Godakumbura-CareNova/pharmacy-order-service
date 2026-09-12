using PharmacyOrder.Api.DTOs.Category;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponseDto>> GetAllAsync();
    Task<CategoryResponseDto> CreateAsync(CategoryRequestDto request);
    Task<CategoryResponseDto> UpdateAsync(int id, CategoryRequestDto request);
    Task DeleteAsync(int id);
}
