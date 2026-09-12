using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Category;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class CategoryServiceTests
{
    [Fact]
    public async Task CreateAsync_AddsCategoryAndCreatedOutboxEvent()
    {
        await using var dbContext = CreateDbContext();
        var service = new CategoryService(dbContext);

        var category = await service.CreateAsync(new CategoryRequestDto { Name = "Pain Relief" });

        Assert.Equal("Pain Relief", category.Name);
        var outboxEvent = await dbContext.EventOutboxEntries.SingleAsync();
        Assert.Equal(EventTypes.CategoryCreated, outboxEvent.EventType);
        Assert.Equal(EventTypes.MedicineTopic, outboxEvent.Topic);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateNamesIgnoringCase()
    {
        await using var dbContext = CreateDbContext();
        var service = new CategoryService(dbContext);
        await service.CreateAsync(new CategoryRequestDto { Name = "Vitamins" });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(new CategoryRequestDto { Name = "vitamins" }));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
