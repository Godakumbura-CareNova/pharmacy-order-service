using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>
/// GCN-31 checkout & order tracking — unit tests.
/// These exercise DTO calculations and OrderService request validation in isolation;
/// no database rows are needed because validation runs before any query is issued.
/// </summary>
public class CheckoutOrderUnitTests
{
    private static readonly Guid Patient = Guid.NewGuid();

    private static OrderService CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OrderService(new AppDbContext(options), new KafkaSettings());
    }

    private static OrderRequestDto ValidRequest() => new()
    {
        RequestId = Guid.NewGuid(),
        Items = new() { new CheckoutItemDto { CartItemId = 1, Quantity = 2, UnitPrice = 200m } }
    };

    // ---------- DTO calculations ----------

    [Fact]
    public void OrderLineDto_LineTotal_multiplies_quantity_by_unit_price()
    {
        var line = new OrderLineDto("Ibuprofen", 3, 150.50m);

        Assert.Equal(451.50m, line.LineTotal);
    }

    [Fact]
    public void CheckoutReviewDto_TotalAmount_sums_all_line_totals()
    {
        var review = new CheckoutReviewDto(new List<CheckoutReviewLineDto>
        {
            new(1, "Ibuprofen", 2, 200m),
            new(2, "Paracetamol", 5, 20m),
            new(3, "Vitamin C", 1, 99.99m)
        });

        Assert.Equal(599.99m, review.TotalAmount);
    }

    [Fact]
    public void CheckoutReviewDto_with_no_items_has_zero_total()
    {
        var review = new CheckoutReviewDto(Array.Empty<CheckoutReviewLineDto>());

        Assert.Empty(review.Items);
        Assert.Equal(0m, review.TotalAmount);
    }

    // ---------- OrderService.PlaceAsync request validation ----------

    [Fact]
    public async Task PlaceAsync_rejects_empty_request_id()
    {
        var request = ValidRequest();
        request.RequestId = Guid.Empty;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().PlaceAsync(Patient, request));

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaceAsync_rejects_request_with_no_items()
    {
        var request = new OrderRequestDto { RequestId = Guid.NewGuid(), Items = new() };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().PlaceAsync(Patient, request));

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaceAsync_rejects_zero_quantity_or_negative_price()
    {
        var zeroQuantity = ValidRequest();
        zeroQuantity.Items[0].Quantity = 0;
        var negativePrice = ValidRequest();
        negativePrice.Items[0].UnitPrice = -0.01m;

        var first = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().PlaceAsync(Patient, zeroQuantity));
        var second = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().PlaceAsync(Patient, negativePrice));

        Assert.Contains("invalid", first.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaceAsync_rejects_duplicate_cart_item_ids()
    {
        var request = ValidRequest();
        request.Items.Add(new CheckoutItemDto { CartItemId = 1, Quantity = 1, UnitPrice = 200m });

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().PlaceAsync(Patient, request));

        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaceAsync_rejects_more_than_200_items()
    {
        var request = new OrderRequestDto
        {
            RequestId = Guid.NewGuid(),
            Items = Enumerable.Range(1, 201)
                .Select(id => new CheckoutItemDto { CartItemId = id, Quantity = 1, UnitPrice = 10m })
                .ToList()
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().PlaceAsync(Patient, request));

        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
