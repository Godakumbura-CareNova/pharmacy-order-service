using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Security;

namespace PharmacyOrder.Api.Tests.Integration;

/// <summary>
/// Boots the real API pipeline (routing, JWT auth, Patient policy, controllers, CartService)
/// with the MySQL context swapped for EF InMemory and Kafka disabled.
/// </summary>
public class CartApiFactory : WebApplicationFactory<AppDbContext>, IDisposable
{
    public const string JwtSecret = "integration-test-jwt-secret-that-is-at-least-32-characters";
    public const string Issuer = "CareNova.UserAuth";
    public const string Audience = "CareNova.Client";

    public string UploadPath { get; } = Path.Combine(Path.GetTempPath(), "carenova-cart-it", Guid.NewGuid().ToString("N"));

    private readonly string _databaseName = $"CartIntegration-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("JwtSettings:Secret", JwtSecret);
        builder.UseSetting("JwtSettings:Issuer", Issuer);
        builder.UseSetting("JwtSettings:Audience", Audience);
        builder.UseSetting("Kafka:Enabled", "false");
        builder.UseSetting("PrescriptionStorage:UploadPath", UploadPath);
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;User=unused;Password=unused;");

        builder.ConfigureServices(services =>
        {
            // dev has no Kafka:Enabled switch; remove the Kafka background services so tests never touch a broker.
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                && d.ImplementationType is { } t && (t == typeof(PharmacyOrder.Api.Services.OutboxPublisherService)
                    || t == typeof(PharmacyOrder.Api.Services.KafkaConsumerService))).ToList())
            {
                services.Remove(hosted);
            }
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)).ToList())
            {
                services.Remove(descriptor);
            }
            services.AddDbContext<AppDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }

    /// <summary>Client signed in as a customer; the patient id is the token's <c>sub</c> claim.</summary>
    public HttpClient CreatePatientClient(Guid? patientId = null) => CreateClientAs(AuthorizationPolicies.Patient, patientId ?? Guid.NewGuid());

    public HttpClient CreateClientAs(string? role, Guid? subject = null)
    {
        var client = CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role, subject ?? Guid.NewGuid()));
        }
        return client;
    }

    public async Task<Medicine> SeedMedicineAsync(string name, bool prescriptionRequired = true, int stock = 10, bool active = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = new Medicine
        {
            Name = name,
            Description = $"{name} description",
            Category = new Category { Name = $"Cat-{name}" },
            Price = 250m,
            PrescriptionRequired = prescriptionRequired,
            StockQuantity = stock,
            IsActive = active
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine;
    }

    public async Task UpdateMedicineAsync(int medicineId, Action<Medicine> change)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.Medicines.SingleAsync(m => m.Id == medicineId));
        await db.SaveChangesAsync();
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static string CreateToken(string role, Guid subject)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "patient.catalogue.2026@example.com"),
                new Claim(ClaimTypes.Role, role)
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(UploadPath)) Directory.Delete(UploadPath, recursive: true);
    }
}
