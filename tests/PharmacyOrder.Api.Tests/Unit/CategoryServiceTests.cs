using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Category;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class CategoryServiceTests
{
    [Fact]
    public async Task CreateAsync_AddsCategoryAndPublishesEvent()
    {
        await using var dbContext = CreateDbContext();
        var service = new CategoryService(dbContext);

        var category = await service.CreateAsync(new CategoryRequestDto { Name = "Pain Relief" });

        Assert.Equal("Pain Relief", category.Name);
        var outboxEvent = await dbContext.EventOutboxEntries.SingleAsync();
        Assert.Equal(EventTypes.CategoryCreated, outboxEvent.EventType);
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

    [Fact]
    public async Task UpdateAsync_RejectsRenamingToExistingCategoryName()
    {
        await using var dbContext = CreateDbContext();
        var service = new CategoryService(dbContext);
        await service.CreateAsync(new CategoryRequestDto { Name = "Vitamins" });
        var painRelief = await service.CreateAsync(new CategoryRequestDto { Name = "Pain Relief" });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateAsync(painRelief.Id, new CategoryRequestDto { Name = "Vitamins" }));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenCategoryHasActiveMedicine()
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Antibiotics" };
        dbContext.Categories.Add(category);
        dbContext.Medicines.Add(new Medicine { Name = "Amoxicillin", Description = "Antibiotic", Category = category, Price = 12m, IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new CategoryService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(category.Id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesCategoryAndItNoLongerAppearsInGetAll()
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Antibiotics" };
        dbContext.Categories.Add(category);
        dbContext.Medicines.Add(new Medicine { Name = "Amoxicillin", Description = "Antibiotic", Category = category, Price = 12m, IsActive = false });
        await dbContext.SaveChangesAsync();
        var service = new CategoryService(dbContext);

        await service.DeleteAsync(category.Id);

        var remaining = await service.GetAllAsync();
        Assert.DoesNotContain(remaining, c => c.Id == category.Id);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }
}