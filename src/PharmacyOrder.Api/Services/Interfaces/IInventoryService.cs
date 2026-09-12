using PharmacyOrder.Api.DTOs.Inventory;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface IInventoryService
{
    Task<IReadOnlyList<MedicineInventoryResponseDto>> GetInventoryAsync(bool lowStockOnly);
    Task<IReadOnlyList<StockBatchResponseDto>> GetBatchesAsync(int medicineId);
    Task<StockBatchResponseDto> CreateBatchAsync(StockBatchRequestDto request);
    Task<StockBatchResponseDto> UpdateBatchAsync(int id, StockBatchRequestDto request);
}
