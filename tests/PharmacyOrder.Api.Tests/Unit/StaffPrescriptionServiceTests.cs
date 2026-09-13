using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Staff;
using PharmacyOrder.Api.Events;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class StaffPrescriptionServiceTests
{
    [Fact]
    public async Task Verify_pending_prescription_changes_status_and_queues_private_event()
    {
        await using var fixture = await Fixture.Create();
        var db = fixture.Db;
        db.Prescriptions.Add(new Prescription
        {
            Id = 7, UploadedBy = Guid.NewGuid(), MedicineId = 4, Quantity = 2,
            OriginalFileName = "doctor-note.pdf", FilePath = "prescriptions/private.pdf"
        });
        await db.SaveChangesAsync();

        var result = await new StaffPrescriptionService(db).DecideAsync(7,
            new PrescriptionDecisionRequest { Decision = "Verified" });

        Assert.Equal("Verified", result.Status);
        Assert.True((await db.Prescriptions.FindAsync(7))!.IsVerified);
        var entry = Assert.Single(await db.EventOutboxEntries.ToListAsync());
        Assert.Equal(EventTypes.PrescriptionVerified, entry.EventType);
        Assert.Equal("7", entry.EventKey);
        using var json = JsonDocument.Parse(entry.PayloadJson);
        Assert.Equal(7, json.RootElement.GetProperty("AggregateId").GetInt32());
        Assert.DoesNotContain("private.pdf", entry.PayloadJson);
    }

    [Theory]
    [InlineData("Rejected", "")]
    [InlineData("Invalid", "pharmacy note")]
    public async Task Invalid_decision_leaves_pending_prescription_unchanged(string decision, string reason)
    {
        await using var fixture = await Fixture.Create();
        var db = fixture.Db;
        db.Prescriptions.Add(new Prescription { Id = 7, UploadedBy = Guid.NewGuid(), MedicineId = 4, Quantity = 2 });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => new StaffPrescriptionService(db).DecideAsync(7,
            new PrescriptionDecisionRequest { Decision = decision, Reason = reason }));

        Assert.Equal("PendingVerification", (await db.Prescriptions.FindAsync(7))!.Status);
        Assert.Empty(await db.EventOutboxEntries.ToListAsync());
    }

    [Fact]
    public async Task Second_decision_is_rejected_without_another_event()
    {
        await using var fixture = await Fixture.Create();
        var db = fixture.Db;
        db.Prescriptions.Add(new Prescription { Id = 7, UploadedBy = Guid.NewGuid(), MedicineId = 4, Quantity = 2, Status = "Verified", IsVerified = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new StaffPrescriptionService(db).DecideAsync(7,
            new PrescriptionDecisionRequest { Decision = "Rejected", Reason = "Already reviewed" }));

        Assert.Empty(await db.EventOutboxEntries.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AppDbContext Db { get; }

        private Fixture(SqliteConnection connection)
        {
            this.connection = connection;
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        }

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.Db.Categories.Add(new Category { Id = 1, Name = "Antibiotics" });
            fixture.Db.Medicines.Add(new Medicine { Id = 4, CategoryId = 1, Name = "Amoxicillin", StockQuantity = 10 });
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
