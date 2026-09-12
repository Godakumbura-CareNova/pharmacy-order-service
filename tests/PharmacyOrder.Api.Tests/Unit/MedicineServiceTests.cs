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
    public async Task CreateAsync_AddsMedicineAndCreatedOutboxEvent()
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
            Price = 25m,
            PrescriptionRequired = false
        });

        Assert.Equal("Paracetamol", medicine.Name);
        Assert.True(medicine.IsActive);
        Assert.Equal(EventTypes.MedicineCreated, (await dbContext.EventOutboxEntries.SingleAsync()).EventType);
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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
