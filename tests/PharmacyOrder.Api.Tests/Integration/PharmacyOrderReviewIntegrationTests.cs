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
using Moq;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Controllers;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Order;
using PharmacyOrder.Api.DTOs.Patient;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Security;
using PharmacyOrder.Api.Services;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Tests.Integration;

/// <summary>
/// GCN-32 pharmacy order review — HTTP integration tests.
/// Real StaffOrderController / StaffPrescriptionController / OrderController /
/// PatientNotificationController over in-memory SQLite, with a header-based test auth scheme.
///
/// Seed: patient owns order 1 (Pending, Gaviscon x2, 2 units allocated from batch 1)
/// and order 2 (ReadyForCollection); prescription 1 is pending verification.
/// </summary>
public class PharmacyOrderReviewIntegrationTests
{
    [Fact]
    public async Task Staff_order_queue_is_only_for_pharmacy_staff()
    {
        await using var app = await App.Create();
        var expectations = new (string? role, HttpStatusCode expected)[]
        {
            (null, HttpStatusCode.Unauthorized), ("Patient", HttpStatusCode.Forbidden), ("Doctor", HttpStatusCode.Forbidden)
        };
        foreach (var (role, expected) in expectations)
        {
            using var client = app.Client(role);
            Assert.Equal(expected, (await client.GetAsync("/api/staff/orders")).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("ReadyForCollection"))).StatusCode);
            Assert.Equal(expected, (await client.GetAsync("/api/staff/prescriptions")).StatusCode);
        }
    }

    [Fact]
    public async Task Staff_queue_lists_pending_and_ready_orders_newest_first()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var queue = await staff.GetFromJsonAsync<List<StaffOrderDto>>("/api/staff/orders");

        Assert.Equal(new[] { "CN-000002", "CN-000001" }, queue!.Select(order => order.OrderNumber));
        Assert.Equal(new[] { "ReadyForCollection", "Pending" }, queue.Select(order => order.Status));
        var pending = queue.Single(order => order.Id == 1);
        Assert.Equal(4200m, pending.TotalAmount);
        Assert.Equal("Gaviscon", Assert.Single(pending.Items).MedicineName);
    }

    [Fact]
    public async Task Marking_ready_updates_status_and_queues_an_OrderReadyForCollection_event()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var response = await staff.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("ReadyForCollection"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ReadyForCollection", (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!.Status);
        using var scope = app.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = Assert.Single(await db.EventOutboxEntries.Where(e => e.EventType == EventTypes.OrderReadyForCollection).ToListAsync());
        Assert.Equal("1", entry.EventKey);
        Assert.Equal(EventTypes.OrderTopic, entry.Topic);
    }

    [Fact]
    public async Task Status_value_is_case_insensitive()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var response = await staff.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("readyforcollection"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ReadyForCollection", (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!.Status);
    }

    [Fact]
    public async Task Rejecting_without_a_reason_is_a_bad_request_and_changes_nothing()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var response = await staff.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("Rejected"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("rejection reason", await response.Content.ReadAsStringAsync());
        var queue = await staff.GetFromJsonAsync<List<StaffOrderDto>>("/api/staff/orders");
        Assert.Equal("Pending", queue!.Single(order => order.Id == 1).Status);
    }

    [Fact]
    public async Task Rejecting_a_pending_order_stores_the_reason_and_restores_stock()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var response = await staff.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("Rejected", "  Batch recalled  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal("Rejected", order.Status);
        Assert.Equal("Batch recalled", order.RejectionReason);
        using var scope = app.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(100, (await db.Medicines.SingleAsync(m => m.Id == 1)).StockQuantity);
        Assert.Equal(100, (await db.Stocks.SingleAsync(s => s.Id == 1)).Quantity);
        Assert.Single(await db.EventOutboxEntries.Where(e => e.EventType == EventTypes.OrderRejected).ToListAsync());
        Assert.DoesNotContain(await staff.GetFromJsonAsync<List<StaffOrderDto>>("/api/staff/orders") ?? new(), o => o.Id == 1);
    }

    [Fact]
    public async Task Pending_order_cannot_jump_straight_to_collected()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var response = await staff.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("Collected"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("not allowed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_order_can_be_collected_and_then_leaves_the_active_queue()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var response = await staff.PutAsJsonAsync("/api/staff/orders/2/status", App.Status("Collected"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Collected", (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!.Status);
        var queue = await staff.GetFromJsonAsync<List<StaffOrderDto>>("/api/staff/orders");
        Assert.DoesNotContain(queue!, order => order.Id == 2);
        using var scope = app.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.EventOutboxEntries.Where(e => e.EventType == EventTypes.OrderCollected).ToListAsync());
    }

    [Fact]
    public async Task Ready_order_can_no_longer_be_rejected()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var response = await staff.PutAsJsonAsync("/api/staff/orders/2/status", App.Status("Rejected", "Changed my mind"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_order_and_invalid_status_return_actionable_errors()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");

        var missing = await staff.PutAsJsonAsync("/api/staff/orders/999/status", App.Status("ReadyForCollection"));
        var invalid = await staff.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("Shipped"));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("ReadyForCollection, Rejected, or Collected", await invalid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_can_verify_a_pending_prescription_once()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");
        var before = await staff.GetFromJsonAsync<List<StaffPrescriptionDto>>("/api/staff/prescriptions");
        Assert.Equal(1, Assert.Single(before!).Id);

        var verify = await staff.PutAsJsonAsync("/api/staff/prescriptions/1/decision", new PrescriptionDecisionRequest { Decision = "Verified" });
        var again = await staff.PutAsJsonAsync("/api/staff/prescriptions/1/decision", new PrescriptionDecisionRequest { Decision = "Verified" });

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Equal("Verified", (await verify.Content.ReadFromJsonAsync<StaffPrescriptionDto>())!.Status);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Empty((await staff.GetFromJsonAsync<List<StaffPrescriptionDto>>("/api/staff/prescriptions"))!);
        var reject = await staff.PutAsJsonAsync("/api/staff/prescriptions/1/decision", new PrescriptionDecisionRequest { Decision = "Rejected" });
        Assert.Equal(HttpStatusCode.BadRequest, reject.StatusCode);
    }

    [Fact]
    public async Task Patient_receives_and_can_read_the_ready_for_collection_notification()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");
        using var patient = app.Client("Patient");
        using var stranger = app.Client("Patient", Guid.NewGuid().ToString());
        await staff.PutAsJsonAsync("/api/staff/orders/1/status", App.Status("ReadyForCollection"));

        // Simulate the Kafka consumer delivering the queued event.
        using (var scope = app.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.EventOutboxEntries.SingleAsync(e => e.EventType == EventTypes.OrderReadyForCollection);
            await new KafkaNotificationHandler(db).HandleAsync(entry.Topic, entry.PayloadJson);
        }

        var mine = await patient.GetFromJsonAsync<List<PatientNotificationDto>>("/api/patient-notifications");
        var notification = Assert.Single(mine!);
        Assert.Equal("Your order is ready for collection", notification.Title);
        Assert.Equal(1, notification.RelatedEntityId);
        Assert.False(notification.IsRead);
        Assert.Equal("ReadyForCollection", (await patient.GetFromJsonAsync<OrderResponseDto>("/api/orders/1"))!.Status);
        Assert.Empty((await stranger.GetFromJsonAsync<List<PatientNotificationDto>>("/api/patient-notifications"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsync($"/api/patient-notifications/{notification.Id}/read", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await patient.PutAsync($"/api/patient-notifications/{notification.Id}/read", null)).StatusCode);
        Assert.True(Assert.Single((await patient.GetFromJsonAsync<List<PatientNotificationDto>>("/api/patient-notifications"))!).IsRead);
    }

    [Fact]
    public async Task Pharmacy_staff_can_list_the_new_order_notification()
    {
        await using var app = await App.Create();
        using var staff = app.Client("PharmacyStaff");
        using (var scope = app.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.StaffNotifications.Add(new StaffNotification
            {
                EventId = Guid.NewGuid(), NotificationType = EventTypes.OrderPlaced,
                Title = "New order received", Message = "Order #1 is pending pharmacy review.",
                RelatedEntityType = "Order", RelatedEntityId = 1, CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await staff.GetAsync("/api/staff/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("New order received", await response.Content.ReadAsStringAsync());
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

        public static StaffOrderStatusRequest Status(string status, string? reason = null) => new() { Status = status, Reason = reason };

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
                services.AddSingleton(Mock.Of<IFileStorageService>());
                services.AddScoped<OrderService>();
                services.AddScoped<StaffOrderService>();
                services.AddScoped<StaffPrescriptionService>();
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
                services.AddPharmacyStaffAuthorization();
                services.AddControllers().AddApplicationPart(typeof(StaffOrderController).Assembly);
            }).Configure(app =>
            {
                app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Categories.Add(new Category { Id = 1, Name = "Digestive" });
                db.Medicines.Add(new Medicine { Id = 1, CategoryId = 1, Name = "Gaviscon", Price = 2100m, StockQuantity = 98 });
                db.Medicines.Add(new Medicine { Id = 2, CategoryId = 1, Name = "Amoxicillin", Price = 500m, StockQuantity = 50, PrescriptionRequired = true });
                db.Stocks.Add(new Stock { Id = 1, MedicineId = 1, BatchNumber = "GV-01", Quantity = 98, ExpiryDate = DateTime.UtcNow.AddMonths(6) });
                db.Orders.Add(new Order
                {
                    Id = 1, CustomerId = patient, RequestId = Guid.NewGuid(), OrderDate = DateTime.UtcNow.AddMinutes(-10),
                    Status = OrderStatus.Pending, TotalAmount = 4200m,
                    Items = new List<OrderItem> { new()
                    {
                        Id = 1, MedicineId = 1, MedicineName = "Gaviscon", Quantity = 2, UnitPrice = 2100m,
                        Allocations = new List<OrderStockAllocation> { new() { Id = 1, StockId = 1, Quantity = 2 } }
                    } }
                });
                db.Orders.Add(new Order
                {
                    Id = 2, CustomerId = patient, RequestId = Guid.NewGuid(), OrderDate = DateTime.UtcNow.AddMinutes(-5),
                    Status = OrderStatus.ReadyForCollection, TotalAmount = 2100m,
                    Items = new List<OrderItem> { new() { Id = 2, MedicineId = 1, MedicineName = "Gaviscon", Quantity = 1, UnitPrice = 2100m } }
                });
                db.Prescriptions.Add(new Prescription
                {
                    Id = 1, UploadedBy = patient, MedicineId = 2, Quantity = 1, OriginalFileName = "rx.pdf",
                    FilePath = "prescriptions/rx.pdf", UploadedAt = DateTime.UtcNow.AddHours(-1)
                });
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
