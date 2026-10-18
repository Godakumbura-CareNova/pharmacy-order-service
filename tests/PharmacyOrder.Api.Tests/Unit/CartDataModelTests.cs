using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Tests.Unit;

public class CartDataModelTests
{
    [Fact]
    public async Task Cart_PersistsGuidPatientOwnership()
    {
        var patientId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.Carts.Add(new Cart { CustomerId = patientId });
        await db.SaveChangesAsync();

        var saved = await db.Carts.SingleAsync();

        Assert.Equal(patientId, saved.CustomerId);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
