using System.Net;
using System.Net.Http.Json;
using PharmacyOrder.Api.DTOs.Cart;

namespace PharmacyOrder.Api.Tests.Integration;

/// <summary>GCN-30: cart view / update / remove through the real HTTP pipeline. Each test uses its own customer id, so carts never collide.</summary>
public class CartControllerIntegrationTests : IClassFixture<CartApiFactory>
{
    private const string CartUrl = "/api/cart";
    private const string ItemsUrl = "/api/cart/items";

    private readonly CartApiFactory _factory;
    private readonly HttpClient _customer;

    public CartControllerIntegrationTests(CartApiFactory factory)
    {
        _factory = factory;
        _customer = factory.CreatePatientClient(Guid.NewGuid());
    }

    private async Task<CartResponseDto> AddAsync(int medicineId, int quantity)
    {
        var response = await _customer.PostAsJsonAsync(ItemsUrl, new { medicineId, quantity });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponseDto>())!;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return body!["message"].ToString()!;
    }

    // 1 — a visitor without a token has no cart
    [Fact]
    public async Task GetCart_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClientAs(role: null).GetAsync(CartUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 2 — "Cart is empty → appropriate empty-cart message": the API returns no items and a zero total
    [Fact]
    public async Task GetCart_NewCustomer_ReturnsEmptyCartWithZeroTotal()
    {
        var cart = await _customer.GetFromJsonAsync<CartResponseDto>(CartUrl);

        Assert.Empty(cart!.ReadyItems);
        Assert.Empty(cart.PendingItems);
        Assert.Empty(cart.AttentionItems);
        Assert.Equal(0m, cart.CartTotal);
    }

    // 3 — Scenario 1: items, quantities and the total are all returned
    [Fact]
    public async Task GetCart_WithItems_ReturnsItemsQuantitiesAndTotal()
    {
        var paracetamol = await _factory.SeedMedicineAsync("IT-Cart-Paracetamol", prescriptionRequired: false, stock: 10);
        var vitaminC = await _factory.SeedMedicineAsync("IT-Cart-VitaminC", prescriptionRequired: false, stock: 10);
        await AddAsync(paracetamol.Id, 2);
        await AddAsync(vitaminC.Id, 1);

        var cart = await _customer.GetFromJsonAsync<CartResponseDto>(CartUrl);

        Assert.Equal(2, cart!.ReadyItems.Count);
        var line = cart.ReadyItems.Single(i => i.MedicineId == paracetamol.Id);
        Assert.Equal("IT-Cart-Paracetamol", line.MedicineName);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(250m, line.UnitPrice);
        Assert.Equal(500m, line.LineTotal);
        Assert.Equal(750m, cart.CartTotal);
    }

    // 4 — Scenario 2: updating a quantity within stock recalculates the total
    [Fact]
    public async Task UpdateItem_WithinStock_Returns200AndRecalculatedTotal()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Cart-Update", prescriptionRequired: false, stock: 10);
        var itemId = (await AddAsync(medicine.Id, 1)).ReadyItems.Single().Id;

        var response = await _customer.PutAsJsonAsync($"{ItemsUrl}/{itemId}", new { quantity = 4 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cart = await response.Content.ReadFromJsonAsync<CartResponseDto>();
        Assert.Equal(4, cart!.ReadyItems.Single().Quantity);
        Assert.Equal(1000m, cart.CartTotal);
    }

    // 5 — "Updated quantity exceeds available stock → error message and prevent the update"
    [Fact]
    public async Task UpdateItem_AboveStock_Returns400AndLeavesCartUnchanged()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Cart-Stock", prescriptionRequired: false, stock: 3);
        var itemId = (await AddAsync(medicine.Id, 2)).ReadyItems.Single().Id;

        var response = await _customer.PutAsJsonAsync($"{ItemsUrl}/{itemId}", new { quantity = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Quantity must be between 1 and 3.", await MessageAsync(response));
        var cart = await _customer.GetFromJsonAsync<CartResponseDto>(CartUrl);
        Assert.Equal(2, cart!.ReadyItems.Single().Quantity);
    }

    // 6 — Scenario 3: removing an item recalculates the total; another customer cannot remove it
    [Fact]
    public async Task RemoveItem_RemovesAndRecalculates_OtherCustomerGets404()
    {
        var keep = await _factory.SeedMedicineAsync("IT-Cart-Keep", prescriptionRequired: false, stock: 10);
        var drop = await _factory.SeedMedicineAsync("IT-Cart-Drop", prescriptionRequired: false, stock: 10);
        await AddAsync(keep.Id, 1);
        var dropId = (await AddAsync(drop.Id, 2)).ReadyItems.Single(i => i.MedicineId == drop.Id).Id;

        var stranger = await _factory.CreatePatientClient(Guid.NewGuid()).DeleteAsync($"{ItemsUrl}/{dropId}");
        var response = await _customer.DeleteAsync($"{ItemsUrl}/{dropId}");

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cart = await response.Content.ReadFromJsonAsync<CartResponseDto>();
        Assert.Equal(keep.Id, Assert.Single(cart!.ReadyItems).MedicineId);
        Assert.Equal(250m, cart.CartTotal);
    }

    // 7 — prescription medicines cannot be added to the cart directly (they go through prescription upload)
    [Fact]
    public async Task AddItem_PrescriptionMedicine_Returns400()
    {
        var antibiotic = await _factory.SeedMedicineAsync("IT-Cart-Rx", prescriptionRequired: true, stock: 10);

        var response = await _customer.PostAsJsonAsync(ItemsUrl, new { medicineId = antibiotic.Id, quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Prescription medicines must be added through prescription upload.", await MessageAsync(response));
    }
}
