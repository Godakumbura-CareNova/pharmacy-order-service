using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.Models;
namespace PharmacyOrder.Api.Services;
public class OrderService
{
    private readonly AppDbContext _db;
    private readonly KafkaSettings _settings;
    public OrderService(AppDbContext db, KafkaSettings settings) { _db = db; _settings = settings; }

    public async Task<CheckoutReviewDto> ReviewAsync(Guid patientId, CancellationToken ct = default)
    {
        var items = await CartQuery(patientId).AsNoTracking().ToListAsync(ct);
        var ready = items.Where(item => item.Medicine != null && item.Medicine.IsActive &&
            (!item.Medicine.PrescriptionRequired && item.PrescriptionId == null ||
             item.Prescription?.Status == "Verified" && item.Prescription.UploadedBy == patientId &&
             item.Prescription.MedicineId == item.MedicineId && item.Quantity <= item.Prescription.Quantity));
        return new CheckoutReviewDto(ready.Select(item => new CheckoutReviewLineDto(
            item.Id, item.Medicine!.Name, item.Quantity, item.Medicine.Price)).ToList());
    }

    public async Task<OrderResponseDto> PlaceAsync(Guid patientId, OrderRequestDto request, CancellationToken ct = default)
    {
        if (request.RequestId == Guid.Empty || request.Items == null || request.Items.Count == 0)
            throw new ArgumentException("Your checkout is empty. Add ready medicines before placing an order.");
        if (request.Items.Count > 200 || request.Items.Any(item => item == null || item.CartItemId <= 0 || item.Quantity < 1 || item.UnitPrice < 0) ||
            request.Items.Select(item => item.CartItemId).Distinct().Count() != request.Items.Count)
            throw new ArgumentException("Checkout items and quantities are invalid.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(request.Items.OrderBy(item => item.CartItemId).Select(item => new
            {
                item.CartItemId,
                item.Quantity,
                UnitPrice = item.UnitPrice.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)
            })))));
        _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var previous = await _db.Orders.Include(order => order.Items)
                .SingleOrDefaultAsync(order => order.CustomerId == patientId && order.RequestId == request.RequestId, ct);
            if (previous != null)
            {
                if (previous.RequestFingerprint != fingerprint)
                    throw new InvalidOperationException("This checkout request was already used with different items.");
                return ToDto(previous);
            }
            var ids = request.Items.Select(item => item.CartItemId).ToList();
            var items = await CartQuery(patientId).Where(item => ids.Contains(item.Id))
                .OrderBy(item => item.MedicineId).ThenBy(item => item.Id).ToListAsync(ct);
            if (items.Count != ids.Count) throw new KeyNotFoundException("A selected cart item was not found. Return to your cart.");
            var order = new Order { CustomerId = patientId, RequestId = request.RequestId,
                RequestFingerprint = fingerprint, OrderDate = DateTime.UtcNow, Status = OrderStatus.Pending };
            foreach (var item in items)
            {
                var reviewed = request.Items.Single(line => line.CartItemId == item.Id);
                var medicine = item.Medicine;
                if (medicine == null || !medicine.IsActive) throw new ArgumentException("A selected medicine is no longer available.");
                if (reviewed.Quantity != item.Quantity || reviewed.UnitPrice != medicine.Price)
                    throw new InvalidOperationException("Your cart quantity or price changed. Return to checkout and review it again.");
                if (medicine.PrescriptionRequired || item.PrescriptionId != null)
                {
                    var prescription = item.Prescription;
                    if (prescription == null || prescription.UploadedBy != patientId || prescription.MedicineId != medicine.Id ||
                        prescription.Status != "Verified" || item.Quantity > prescription.Quantity)
                        throw new ArgumentException($"An approved prescription is required for {medicine.Name}.");
                    if (await _db.OrderItems.AnyAsync(line => line.PrescriptionId == prescription.Id &&
                        line.Order!.Status != OrderStatus.Rejected, ct))
                        throw new ArgumentException("This prescription has already been used for an order.");
                }
                order.Items.Add(new OrderItem { MedicineId = medicine.Id, MedicineName = medicine.Name,
                    Quantity = item.Quantity, UnitPrice = medicine.Price, PrescriptionId = item.PrescriptionId });
            }
            if (items.Where(item => item.PrescriptionId != null).GroupBy(item => item.PrescriptionId).Any(group => group.Count() > 1))
                throw new ArgumentException("A prescription cannot be used for multiple checkout items.");
            foreach (var group in order.Items.GroupBy(item => item.MedicineId).OrderBy(group => group.Key))
            {
                var medicine = items.First(item => item.MedicineId == group.Key).Medicine!;
                var needed = group.Sum(item => (long)item.Quantity);
                if (needed > medicine.StockQuantity) throw new InvalidOperationException($"Insufficient stock for {medicine.Name}. Please update your cart.");
                var today = DateTime.UtcNow.Date;
                var batches = await _db.Stocks.Where(stock => stock.MedicineId == group.Key && stock.Quantity > 0 &&
                    stock.ExpiryDate > today).OrderBy(stock => stock.ExpiryDate).ThenBy(stock => stock.Id).ToListAsync(ct);
                if (batches.Sum(stock => (long)stock.Quantity) < needed)
                    throw new InvalidOperationException($"Insufficient unexpired stock for {medicine.Name}.");
                foreach (var line in group)
                {
                    var remaining = line.Quantity;
                    foreach (var batch in batches)
                    {
                        var take = Math.Min(remaining, batch.Quantity);
                        if (take == 0) continue;
                        batch.Quantity -= take;
                        line.Allocations.Add(new OrderStockAllocation { StockId = batch.Id, Quantity = take });
                        remaining -= take;
                        if (remaining == 0) break;
                    }
                }
                medicine.StockQuantity -= (int)needed;
                AddInventoryEvents(medicine);
            }
            order.TotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
            _db.Orders.Add(order);
            _db.CartItems.RemoveRange(items);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToDto(order);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderResponseDto>> ListAsync(Guid patientId, CancellationToken ct = default)
    {
        var orders = await _db.Orders.AsNoTracking().Include(order => order.Items)
            .Where(order => order.CustomerId == patientId).OrderByDescending(order => order.Id).ToListAsync(ct);
        return orders.Select(ToDto).ToList();
    }

    public async Task<OrderResponseDto> GetAsync(Guid patientId, int id, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking().Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.Id == id && order.CustomerId == patientId, ct)
            ?? throw new KeyNotFoundException("Order was not found.");
        return ToDto(order);
    }

    // Called by authorized staff workflows, never exposed through a Patient status endpoint.
    public async Task<OrderResponseDto> TransitionAsync(int id, OrderStatus status, string? reason, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var order = await _db.Orders.Include(order => order.Items).ThenInclude(item => item.Allocations)
                .ThenInclude(allocation => allocation.Stock).Include(order => order.Items).ThenInclude(item => item.Medicine)
                .SingleOrDefaultAsync(order => order.Id == id, ct) ?? throw new KeyNotFoundException("Order was not found.");
            if (order.Status == status) return ToDto(order);
            var allowed = order.Status == OrderStatus.Pending && (status == OrderStatus.ReadyForCollection || status == OrderStatus.Rejected) ||
                order.Status == OrderStatus.ReadyForCollection && (status == OrderStatus.Collected || status == OrderStatus.Rejected);
            if (!allowed) throw new InvalidOperationException("This order status change is not allowed.");
            if (status == OrderStatus.Rejected)
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
                    throw new ArgumentException("Provide a rejection reason of up to 1000 characters.");
                foreach (var group in order.Items.GroupBy(item => item.MedicineId).OrderBy(group => group.Key))
                {
                    var medicine = group.First().Medicine!;
                    foreach (var line in group)
                        foreach (var allocation in line.Allocations)
                            allocation.Stock!.Quantity = checked(allocation.Stock.Quantity + allocation.Quantity);
                    medicine.StockQuantity = checked(medicine.StockQuantity + group.Sum(item => item.Quantity));
                    AddInventoryEvents(medicine);
                }
                order.RejectionReason = reason.Trim();
            }
            order.Status = status;
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToDto(order);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private IQueryable<CartItem> CartQuery(Guid patientId) => _db.CartItems
        .Where(item => item.Cart!.CustomerId == patientId).Include(item => item.Medicine).Include(item => item.Prescription);

    private static OrderResponseDto ToDto(Order order) => new(order.Id, $"CN-{order.Id:D6}", DateTime.SpecifyKind(order.OrderDate, DateTimeKind.Utc),
        order.Status.ToString(), order.TotalAmount, order.RejectionReason,
        order.Items.OrderBy(item => item.Id).Select(item => new OrderLineDto(item.MedicineName, item.Quantity, item.UnitPrice)).ToList());

    private void AddInventoryEvents(Medicine medicine)
    {
        var types = new List<string> { EventTypes.InventoryUpdated };
        if (medicine.StockQuantity <= _settings.LowStockThreshold) types.Add(EventTypes.LowStockDetected);
        if (medicine.StockQuantity == 0) types.Add(EventTypes.MedicineOutOfStock);
        foreach (var type in types)
        {
            var eventId = Guid.NewGuid();
            var at = DateTime.UtcNow;
            _db.EventOutboxEntries.Add(new EventOutbox { EventId = eventId, Topic = EventTypes.InventoryTopic,
                EventType = type, EventKey = medicine.Id.ToString(), OccurredAtUtc = at,
                PayloadJson = JsonSerializer.Serialize(new InventoryEvent(eventId, type, at, medicine.Id, "Inventory",
                    JsonSerializer.SerializeToElement(new { TotalQuantity = medicine.StockQuantity }))) });
        }
    }
}
