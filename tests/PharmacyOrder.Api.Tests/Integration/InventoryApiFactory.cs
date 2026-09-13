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
/// Boots the real API pipeline (routing, JWT auth, PharmacyStaff policy, controllers, services)
/// with the MySQL context swapped for EF InMemory and Kafka disabled.
/// </summary>
public class InventoryApiFactory : WebApplicationFactory<Program>
{
    public const string JwtSecret = "integration-test-jwt-secret-that-is-at-least-32-characters";
    public const string Issuer = "CareNova.UserAuth";
    public const string Audience = "CareNova.Client";
    public const int LowStockThreshold = 10;

    private readonly string _databaseName = $"InventoryIntegration-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting lands in the host configuration before Program.cs runs, so the
        // JwtSettings check at startup sees these values (ConfigureAppConfiguration would be too late).
        builder.UseSetting("JwtSettings:Secret", JwtSecret);
        builder.UseSetting("JwtSettings:Issuer", Issuer);
        builder.UseSetting("JwtSettings:Audience", Audience);
        builder.UseSetting("Kafka:Enabled", "false");
        builder.UseSetting("Kafka:LowStockThreshold", LowStockThreshold.ToString());
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;User=unused;Password=unused;");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }

    public HttpClient CreateClientAs(string? role)
    {
        var client = CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role));
        }
        return client;
    }

    public HttpClient CreateStaffClient() => CreateClientAs(AuthorizationPolicies.PharmacyStaff);

    public async Task<Medicine> SeedMedicineAsync(string name, int stock = 0)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var medicine = new Medicine { Name = name, Description = "Integration test medicine", IsActive = true, StockQuantity = stock };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();
        return medicine;
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static string CreateToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "pharmacy.staff.2026@example.com"),
                new Claim(ClaimTypes.Role, role)
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

file static class ServiceCollectionExtensions
{
    public static void RemoveAll(this IServiceCollection services, Type serviceType)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == serviceType).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
