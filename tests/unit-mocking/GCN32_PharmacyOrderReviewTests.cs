using Moq;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.UnitMocking.Tests;

// GCN-32 Pharmacy Order Review
public class GCN32_PharmacyOrderReviewTests
{
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly KafkaSettings Settings = new() { LowStockThreshold = 10 };

    private readonly AppDbContext _db = TestDoubles.FakeDb();
    private readonly StaffPrescriptionService _prescriptions;

    public GCN32_PharmacyOrderReviewTests()
    {
        _db.Medicines.Add(new Medicine { Id = 1, Name = "Amoxicillin", Price = 200m, StockQuantity = 20 });
        _db.Prescriptions.Add(new Prescription { Id = 3, MedicineId = 1, UploadedBy = PatientId, Quantity = 1, Status = "PendingVerification" });
        _db.SaveChanges();
        // file storage is a dummy, not used when deciding
        _prescriptions = new StaffPrescriptionService(_db, Mock.Of<IFileStorageService>());
    }

    // only Verified or Rejected with a reason is accepted
    [Theory]
    [InlineData("Rejected", "   ")]
    public async Task DecideAsync_InvalidDecision_IsRejected(string decision, string? reason)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _prescriptions.DecideAsync(3, new PrescriptionDecisionRequest { Decision = decision, Reason = reason }));

        Assert.Equal("PendingVerification", _db.Prescriptions.Single().Status);
    }

    // verifying a prescription raises an event for the patient
    [Fact]
    public async Task DecideAsync_Verified_UpdatesPrescription_AndQueuesEvent()
    {
        var result = await _prescriptions.DecideAsync(3, new PrescriptionDecisionRequest { Decision = "Verified" });

        Assert.Equal("Verified", result.Status);
        Assert.True(_db.Prescriptions.Single().IsVerified);
        Assert.Single(_db.EventOutboxEntries, e => e.EventType == EventTypes.PrescriptionVerified);
    }

    // a decided prescription cannot be decided again
    [Fact]
    public async Task DecideAsync_AlreadyDecided_IsRejected()
    {
        await _prescriptions.DecideAsync(3, new PrescriptionDecisionRequest { Decision = "Rejected", Reason = "Unclear photo" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _prescriptions.DecideAsync(3, new PrescriptionDecisionRequest { Decision = "Verified" }));
    }

    // staff cannot set an order back to pending or use unknown statuses
    [Theory]
    [InlineData("Pending")]
    public async Task UpdateStatusAsync_InvalidStatus_IsRejected(string status)
    {
        var staffOrders = new StaffOrderService(_db, Settings);

        await Assert.ThrowsAsync<ArgumentException>(() => staffOrders.UpdateStatusAsync(1, new StaffOrderStatusRequest { Status = status }));
    }

    // rejecting an order puts the stock back
    [Fact]
    public async Task UpdateStatusAsync_RejectOrder_RestoresStock()
    {
        _db.Medicines.Add(new Medicine { Id = 2, Name = "Panadol", Price = 50m, StockQuantity = 10 });
        _db.Stocks.Add(new Stock { Id = 1, MedicineId = 2, BatchNumber = "B-1", Quantity = 10, ExpiryDate = DateTime.UtcNow.AddYears(1) });
        _db.Carts.Add(new Cart { CustomerId = PatientId, Items = { new CartItem { Id = 8, MedicineId = 2, Quantity = 3 } } });
        _db.SaveChanges();
        var order = await new OrderService(_db, Settings).PlaceAsync(PatientId, new OrderRequestDto
        {
            RequestId = Guid.NewGuid(), Items = { new CheckoutItemDto { CartItemId = 8, Quantity = 3, UnitPrice = 50m } }
        });

        var rejected = await new StaffOrderService(_db, Settings)
            .UpdateStatusAsync(order.Id, new StaffOrderStatusRequest { Status = "Rejected", Reason = "Out of date prescription" });

        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(10, _db.Medicines.Single(m => m.Id == 2).StockQuantity);
        Assert.Equal(10, _db.Stocks.Single().Quantity);
        Assert.Contains(_db.EventOutboxEntries, e => e.EventType == EventTypes.OrderRejected);
    }
}
