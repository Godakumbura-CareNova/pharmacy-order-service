using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Inventory;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly KafkaSettings _settings;
    public InventoryService(AppDbContext db, KafkaSettings settings) { _db = db; _settings = settings; }
    public async Task<IReadOnlyList<MedicineInventoryResponseDto>> GetInventoryAsync(bool lowStockOnly)
    {
        var query = _db.Medicines.AsQueryable();
        if (lowStockOnly) query = query.Where(m => m.StockQuantity <= _settings.LowStockThreshold);
        return await query.OrderBy(m => m.Name).Select(m => new MedicineInventoryResponseDto { MedicineId = m.Id, MedicineName = m.Name, TotalQuantity = m.StockQuantity, IsLowStock = m.StockQuantity <= _settings.LowStockThreshold, AvailabilityStatus = m.StockQuantity == 0 ? "Out of stock" : m.StockQuantity <= _settings.LowStockThreshold ? "Low stock" : "Available" }).ToListAsync();
    }
    public async Task<IReadOnlyList<StockBatchResponseDto>> GetBatchesAsync(int medicineId) => await _db.Stocks.Where(s => s.MedicineId == medicineId).OrderBy(s => s.ExpiryDate).Select(s => ToDto(s)).ToListAsync();
    public async Task<StockBatchResponseDto> CreateBatchAsync(StockBatchRequestDto request)
    {
        await ValidateAsync(request, null);
        var stock = new Stock { MedicineId = request.MedicineId, BatchNumber = request.BatchNumber.Trim(), Quantity = request.Quantity, PurchasePrice = request.PurchasePrice, SellingPrice = request.SellingPrice, ManufacturingDate = request.ManufacturingDate, ExpiryDate = request.ExpiryDate };
        _db.Stocks.Add(stock); await PersistAsync(stock.MedicineId, stock); return ToDto(stock);
    }
    public async Task<StockBatchResponseDto> UpdateBatchAsync(int id, StockBatchRequestDto request)
    {
        var stock = await _db.Stocks.FindAsync(id) ?? throw new KeyNotFoundException("Stock batch was not found.");
        if (stock.MedicineId != request.MedicineId) throw new ArgumentException("A stock batch cannot be moved to another medicine.");
        await ValidateAsync(request, id); stock.BatchNumber = request.BatchNumber.Trim(); stock.Quantity = request.Quantity; stock.PurchasePrice = request.PurchasePrice; stock.SellingPrice = request.SellingPrice; stock.ManufacturingDate = request.ManufacturingDate; stock.ExpiryDate = request.ExpiryDate;
        await PersistAsync(stock.MedicineId, stock); return ToDto(stock);
    }
    private async Task PersistAsync(int medicineId, Stock stock)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var medicine = await _db.Medicines.FindAsync(medicineId) ?? throw new KeyNotFoundException("Medicine was not found.");
        var trackedIds = _db.Stocks.Local.Select(s => s.Id).ToHashSet();
        var trackedTotal = _db.Stocks.Local.Where(s => s.MedicineId == medicineId).Sum(s => s.Quantity);
        var savedTotal = await _db.Stocks
            .Where(s => s.MedicineId == medicineId && !trackedIds.Contains(s.Id))
            .SumAsync(s => (int?)s.Quantity) ?? 0;
        var total = trackedTotal + savedTotal;
        medicine.StockQuantity = total;
        AddEvent(EventTypes.InventoryUpdated, medicineId, stock, total);
        if (total <= _settings.LowStockThreshold) AddEvent(EventTypes.LowStockDetected, medicineId, stock, total);
        if (total == 0) AddEvent(EventTypes.MedicineOutOfStock, medicineId, stock, total);
        await _db.SaveChangesAsync(); await transaction.CommitAsync();
    }
    private void AddEvent(string type, int medicineId, Stock stock, int total)
    {
        var id = Guid.NewGuid(); var at = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new InventoryEvent(id, type, at, medicineId, "Inventory", JsonSerializer.SerializeToElement(new { stock.BatchNumber, stock.Quantity, TotalQuantity = total })));
        _db.EventOutboxEntries.Add(new EventOutbox { EventId = id, Topic = EventTypes.InventoryTopic, EventType = type, EventKey = medicineId.ToString(), PayloadJson = payload, OccurredAtUtc = at });
    }
    private async Task ValidateAsync(StockBatchRequestDto request, int? id)
    {
        if (request.MedicineId <= 0 || !await _db.Medicines.AnyAsync(m => m.Id == request.MedicineId)) throw new KeyNotFoundException("Medicine was not found.");
        if (string.IsNullOrWhiteSpace(request.BatchNumber)) throw new ArgumentException("Batch number is required.");
        if (request.Quantity < 0) throw new ArgumentException("Stock quantity cannot be negative.");
        if (request.PurchasePrice < 0 || request.SellingPrice < 0) throw new ArgumentException("Stock prices cannot be negative.");
        if (request.ExpiryDate < request.ManufacturingDate) throw new ArgumentException("Expiry date cannot be before manufacturing date.");
        if (await _db.Stocks.AnyAsync(s => s.MedicineId == request.MedicineId && s.BatchNumber == request.BatchNumber.Trim() && (!id.HasValue || s.Id != id))) throw new InvalidOperationException("This batch number already exists for the medicine.");
    }
    private static StockBatchResponseDto ToDto(Stock s) => new() { Id = s.Id, MedicineId = s.MedicineId, BatchNumber = s.BatchNumber, Quantity = s.Quantity, PurchasePrice = s.PurchasePrice, SellingPrice = s.SellingPrice, ManufacturingDate = s.ManufacturingDate, ExpiryDate = s.ExpiryDate };
}
