using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Tests.Unit;

public class EventOutboxPersistenceTests
{
    [Fact]
    public void EventOutbox_UsesAUniqueEventIdIndex()
    {
        using var dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(typeof(EventOutbox));

        Assert.NotNull(entityType);
        Assert.Contains(
            entityType.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Count == 1
                && index.Properties[0].Name == nameof(EventOutbox.EventId));
    }

    [Fact]
    public void NewMedicine_IsActiveByDefault()
    {
        var medicine = new Medicine();

        Assert.True(medicine.IsActive);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
