using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Medicine;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class MedicineServiceTests
{
    [Fact]
    public async Task CreateAsync_AddsMedicineAndPublishesEvent()
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Pain Relief" };
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();
        var service = new MedicineService(dbContext);

        var medicine = await service.CreateAsync(new MedicineRequestDto
        {
            Name = "Paracetamol",
            Description = "Pain relief tablets",
            CategoryId = category.Id,
            Price = 25m
        });

        Assert.True(medicine.IsActive);
        Assert.Equal(EventTypes.MedicineCreated, (await dbContext.EventOutboxEntries.SingleAsync()).EventType);
    }

    [Theory]
    [InlineData("missingName")]
    [InlineData("negativePrice")]
    [InlineData("unknownCategory")]
    public async Task CreateAsync_RejectsInvalidInput(string scenario)
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Vitamins" };
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();
        var service = new MedicineService(dbContext);

        var request = scenario switch
        {
            "missingName" => new MedicineRequestDto { Name = " ", Description = "Tablets", CategoryId = category.Id, Price = 10m },
            "negativePrice" => new MedicineRequestDto { Name = "Vitamin C", Description = "Tablets", CategoryId = category.Id, Price = -1m },
            "unknownCategory" => new MedicineRequestDto { Name = "Vitamin C", Description = "Tablets", CategoryId = 999, Price = 10m },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        if (scenario == "unknownCategory")
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(request));
        else
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameRulesAreScopedToCategory()
    {
        await using var dbContext = CreateDbContext();
        var categoryOne = new Category { Name = "Vitamins" };
        var categoryTwo = new Category { Name = "Supplements" };
        dbContext.Categories.AddRange(categoryOne, categoryTwo);
        await dbContext.SaveChangesAsync();
        var service = new MedicineService(dbContext);
        await service.CreateAsync(new MedicineRequestDto { Name = "Vitamin C", Description = "Tablets", CategoryId = categoryOne.Id, Price = 10m });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new MedicineRequestDto { Name = "vitamin c", Description = "Different", CategoryId = categoryOne.Id, Price = 12m }));

        var medicine = await service.CreateAsync(
            new MedicineRequestDto { Name = "Vitamin C", Description = "Chewable", CategoryId = categoryTwo.Id, Price = 12m });
        Assert.Equal("Vitamin C", medicine.Name);
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldsAndPublishesEvent()
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Vitamins" };
        var medicine = new Medicine { Name = "Vitamin C", Description = "Old description", Category = category, Price = 10m };
        dbContext.Medicines.Add(medicine);
        await dbContext.SaveChangesAsync();
        var service = new MedicineService(dbContext);

        var updated = await service.UpdateAsync(medicine.Id, new MedicineRequestDto
        {
            Name = "Vitamin C 1000mg",
            Description = "New description",
            CategoryId = category.Id,
            Price = 15m,
            PrescriptionRequired = true
        });

        Assert.Equal("Vitamin C 1000mg", updated.Name);
        Assert.Equal(15m, updated.Price);
        Assert.Equal(EventTypes.MedicineUpdated, (await dbContext.EventOutboxEntries.SingleAsync()).EventType);
    }

    [Fact]
    public async Task DeactivateAsync_MarksMedicineInactiveAndPublishesEvent()
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Vitamins" };
        var medicine = new Medicine { Name = "Vitamin C", Description = "Vitamin", Category = category, Price = 10m };
        dbContext.Medicines.Add(medicine);
        await dbContext.SaveChangesAsync();
        var service = new MedicineService(dbContext);

        await service.DeactivateAsync(medicine.Id);

        Assert.False((await dbContext.Medicines.FindAsync(medicine.Id))!.IsActive);
        Assert.Equal(EventTypes.MedicineDeactivated, (await dbContext.EventOutboxEntries.SingleAsync()).EventType);
    }

    [Fact]
    public async Task ActivateAsync_MarksMedicineActiveAndPublishesEvent()
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Vitamins" };
        var medicine = new Medicine { Name = "Vitamin C", Description = "Vitamin", Category = category, Price = 10m, IsActive = false };
        dbContext.Medicines.Add(medicine);
        await dbContext.SaveChangesAsync();
        var service = new MedicineService(dbContext);

        await service.ActivateAsync(medicine.Id);

        Assert.True((await dbContext.Medicines.FindAsync(medicine.Id))!.IsActive);
        Assert.Equal(EventTypes.MedicineActivated, (await dbContext.EventOutboxEntries.SingleAsync()).EventType);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesInactiveWhenRequested()
    {
        await using var dbContext = CreateDbContext();
        var category = new Category { Name = "Vitamins" };
        dbContext.Categories.Add(category);
        dbContext.Medicines.AddRange(
            new Medicine { Name = "Active Med", Description = "x", Category = category, Price = 5m, IsActive = true },
            new Medicine { Name = "Inactive Med", Description = "x", Category = category, Price = 5m, IsActive = false });
        await dbContext.SaveChangesAsync();
        var service = new MedicineService(dbContext);

        var activeOnly = await service.GetAllAsync(includeInactive: false);
        var everything = await service.GetAllAsync(includeInactive: true);

        Assert.Single(activeOnly);
        Assert.Equal(2, everything.Count);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }
}