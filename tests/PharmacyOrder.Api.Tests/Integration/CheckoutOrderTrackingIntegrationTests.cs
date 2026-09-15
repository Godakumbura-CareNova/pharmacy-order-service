using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Integration;

/// <summary>
/// GCN-31 checkout & order tracking — HTTP integration tests.
/// Runs the real OrderController + OrderService against an in-memory SQLite database
/// with a header-based test authentication scheme (X-Test-Role / X-Test-User).
/// </summary>
public class CheckoutOrderTrackingIntegrationTests
{
    [Fact]
    public async Task Review_lists_ready_cart_items_with_total()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");

        var review = await client.GetFromJsonAsync<CheckoutReviewDto>("/api/orders/review");

        Assert.NotNull(review);
        var line = Assert.Single(review!.Items);
        Assert.Equal("Ibuprofen", line.MedicineName);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(200m, line.UnitPrice);
        Assert.Equal(400m, review.TotalAmount);
    }

    [Fact]
    public async Task Placing_an_order_returns_pending_order_with_formatted_number()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");

        var response = await client.PostAsJsonAsync("/api/orders", app.Request());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal("Pending", order.Status);
        Assert.Equal($"CN-{order.Id:D6}", order.OrderNumber);
        Assert.Equal(400m, order.TotalAmount);
        Assert.Null(order.RejectionReason);
        Assert.Single(order.Items);
    }

    [Fact]
    public async Task Placing_an_order_clears_the_cart_and_reduces_stock()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");

        await client.PostAsJsonAsync("/api/orders", app.Request());

        using var scope = app.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.CartItems.ToListAsync());
        Assert.Equal(4, (await db.Medicines.SingleAsync(m => m.Id == 1)).StockQuantity);
        Assert.Equal(4, (await db.Stocks.SingleAsync(s => s.Id == 1)).Quantity);
        var review = await client.GetFromJsonAsync<CheckoutReviewDto>("/api/orders/review");
        Assert.Empty(review!.Items);
    }

    [Fact]
    public async Task Placing_an_order_queues_an_OrderPlaced_outbox_event()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");

        var order = (await (await client.PostAsJsonAsync("/api/orders", app.Request()))
            .Content.ReadFromJsonAsync<OrderResponseDto>())!;

        using var scope = app.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = Assert.Single(await db.EventOutboxEntries.Where(e => e.EventType == "OrderPlaced").ToListAsync());
        Assert.Equal("carenova.order.events.v1", entry.Topic);
        Assert.Equal(order.Id.ToString(), entry.EventKey);
        Assert.Null(entry.PublishedAtUtc);
    }

    [Fact]
    public async Task Repeating_the_same_request_id_returns_the_existing_order_without_duplicating()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");
        var request = app.Request();

        var first = (await (await client.PostAsJsonAsync("/api/orders", request)).Content.ReadFromJsonAsync<OrderResponseDto>())!;
        var second = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(first.Id, (await second.Content.ReadFromJsonAsync<OrderResponseDto>())!.Id);
        Assert.Single((await client.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders"))!);
    }

    [Fact]
    public async Task Reusing_a_request_id_with_different_items_is_a_conflict()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");
        var request = app.Request();
        await client.PostAsJsonAsync("/api/orders", request);

        request.Items[0].Quantity = 1;
        var response = await client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already used", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stale_or_unknown_checkout_lines_return_actionable_errors()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");

        var unknownItem = app.Request();
        unknownItem.Items[0].CartItemId = 999;
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/orders", unknownItem)).StatusCode);

        var priceChanged = app.Request();
        priceChanged.Items[0].UnitPrice = 150m;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/orders", priceChanged)).StatusCode);

        var quantityChanged = app.Request();
        quantityChanged.Items[0].Quantity = 3;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/orders", quantityChanged)).StatusCode);

        Assert.Empty((await client.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders"))!);
    }

    [Fact]
    public async Task Order_history_and_details_are_isolated_per_patient()
    {
        await using var app = await App.Create();
        using var owner = app.Client("Patient");
        var order = (await (await owner.PostAsJsonAsync("/api/orders", app.Request()))
            .Content.ReadFromJsonAsync<OrderResponseDto>())!;

        var mine = await owner.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders");
        Assert.Equal(order.Id, Assert.Single(mine!).Id);
        var details = await owner.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}");
        Assert.Equal(order.OrderNumber, details!.OrderNumber);

        using var stranger = app.Client("Patient", Guid.NewGuid().ToString());
        Assert.Empty((await stranger.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/orders/{order.Id}")).StatusCode);
    }

    // ---------- test host ----------

    private sealed class App : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly TestServer server;
        private readonly Guid patient;

        private App(SqliteConnection connection, TestServer server, Guid patient)
        { this.connection = connection; this.server = server; this.patient = patient; }

        public IServiceScope Scope() => server.Services.CreateScope();

        public HttpClient Client(string? role, string? user = null)
        {
            var client = server.CreateClient();
            if (role != null)
            {
                client.DefaultRequestHeaders.Add("X-Test-Role", role);
                client.DefaultRequestHeaders.Add("X-Test-User", user ?? patient.ToString());
            }
            return client;
        }

        public OrderRequestDto Request() => new()
        {
            RequestId = Guid.NewGuid(),
            Items = new() { new CheckoutItemDto { CartItemId = 1, Quantity = 2, UnitPrice = 200m } }
        };

        public static async Task<App> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var patient = Guid.NewGuid();
            var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton(new KafkaSettings());
                services.AddScoped<OrderService>();
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
                services.AddPharmacyStaffAuthorization();
                services.AddControllers().AddApplicationPart(typeof(OrderController).Assembly);
            }).Configure(app =>
            {
                app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Categories.Add(new Category { Id = 1, Name = "Pain relief" });
                db.Medicines.Add(new Medicine { Id = 1, CategoryId = 1, Name = "Ibuprofen", Price = 200m, StockQuantity = 6 });
                db.Stocks.Add(new Stock { Id = 1, MedicineId = 1, BatchNumber = "B-001", Quantity = 6, ExpiryDate = DateTime.UtcNow.AddDays(30) });
                db.Carts.Add(new Cart { Id = 1, CustomerId = patient });
                db.CartItems.Add(new CartItem { Id = 1, CartId = 1, MedicineId = 1, Quantity = 2 });
                await db.SaveChangesAsync();
            }
            return new App(connection, server, patient);
        }

        public async ValueTask DisposeAsync() { server.Dispose(); await connection.DisposeAsync(); }
    }

    private sealed class TestAuth : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Test-User"].ToString())
            }, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
