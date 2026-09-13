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

public class PickupOrderHttpTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("PharmacyStaff", HttpStatusCode.Forbidden)]
    [InlineData("Doctor", HttpStatusCode.Forbidden)]
    public async Task Only_patients_can_access_orders(string? role, HttpStatusCode expected)
    {
        await using var app = await App.Create();
        using var client = app.Client(role);
        Assert.Equal(expected, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/orders", app.Request())).StatusCode);
    }

    [Fact]
    public async Task Malformed_patient_id_is_unauthorized()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient", "not-a-guid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Http_checkout_review_placement_history_and_owner_isolation()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");
        var review = await client.GetFromJsonAsync<CheckoutReviewDto>("/api/orders/review");
        Assert.Equal(400m, review!.TotalAmount);
        Assert.Single(review.Items);
        var request = app.Request();
        var response = await client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal("Pending", order.Status);
        Assert.Equal(400m, order.TotalAmount);
        var repeated = await client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(order.Id, (await repeated.Content.ReadFromJsonAsync<OrderResponseDto>())!.Id);
        Assert.Single((await client.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders"))!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        using var other = app.Client("Patient", Guid.NewGuid().ToString());
        Assert.Empty((await other.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{order.Id}")).StatusCode);
    }

    [Fact]
    public async Task Empty_checkout_and_changed_review_return_actionable_errors()
    {
        await using var app = await App.Create();
        using var client = app.Client("Patient");
        var empty = await client.PostAsJsonAsync("/api/orders", new OrderRequestDto { RequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("empty", await empty.Content.ReadAsStringAsync());
        var changed = app.Request();
        changed.Items[0].UnitPrice = 100;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/orders", changed)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders"))!);
    }

    private sealed class App : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly TestServer server;
        private readonly Guid patient;
        private App(SqliteConnection connection, TestServer server, Guid patient)
        { this.connection = connection; this.server = server; this.patient = patient; }
        public HttpClient Client(string? role, string? user = null)
        {
            var client = server.CreateClient();
            if (role != null) { client.DefaultRequestHeaders.Add("X-Test-Role", role); client.DefaultRequestHeaders.Add("X-Test-User", user ?? patient.ToString()); }
            return client;
        }
        public OrderRequestDto Request() => new() { RequestId = Guid.NewGuid(),
            Items = new() { new CheckoutItemDto { CartItemId = 1, Quantity = 2, UnitPrice = 200m } } };
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
                db.Categories.Add(new Category { Id = 1, Name = "Test" });
                db.Medicines.Add(new Medicine { Id = 1, CategoryId = 1, Name = "Ibuprofen", Price = 200m, StockQuantity = 6 });
                db.Stocks.Add(new Stock { Id = 1, MedicineId = 1, BatchNumber = "TEST", Quantity = 6, ExpiryDate = DateTime.UtcNow.AddDays(30) });
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
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Test-User"].ToString()) }, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
