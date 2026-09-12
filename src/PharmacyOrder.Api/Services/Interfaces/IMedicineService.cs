using PharmacyOrder.Api.DTOs.Medicine;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface IMedicineService
{
    Task<IReadOnlyList<MedicineResponseDto>> GetAllAsync(bool includeInactive);
    Task<MedicineResponseDto> CreateAsync(MedicineRequestDto request);
    Task<MedicineResponseDto> UpdateAsync(int id, MedicineRequestDto request);
    Task DeactivateAsync(int id);
}
