using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Medicine> Medicines { get; set; }

    public DbSet<Category> Categories { get; set; }

    public DbSet<Stock> Stocks { get; set; }

    public DbSet<Cart> Carts { get; set; }

    public DbSet<CartItem> CartItems { get; set; }

    public DbSet<Order> Orders { get; set; }

    public DbSet<OrderItem> OrderItems { get; set; }

    public DbSet<OrderStockAllocation> OrderStockAllocations { get; set; }

    public DbSet<Prescription> Prescriptions { get; set; }

    public DbSet<EventOutbox> EventOutboxEntries { get; set; }

    public DbSet<StaffNotification> StaffNotifications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>().HasIndex(order => new { order.CustomerId, order.RequestId }).IsUnique();
        modelBuilder.Entity<Order>().Property(order => order.RequestFingerprint).HasMaxLength(64);
        modelBuilder.Entity<Order>().Property(order => order.Status).IsConcurrencyToken();
        modelBuilder.Entity<Order>().Property(order => order.RejectionReason).HasMaxLength(1000);
        modelBuilder.Entity<Prescription>().Property(prescription => prescription.DecisionReason).HasMaxLength(1000);
        modelBuilder.Entity<OrderItem>().HasOne(item => item.Prescription).WithMany()
            .HasForeignKey(item => item.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderStockAllocation>().HasOne(item => item.Stock).WithMany()
            .HasForeignKey(item => item.StockId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Stock>().Property(stock => stock.Quantity).IsConcurrencyToken();
        modelBuilder.Entity<Medicine>().Property(medicine => medicine.StockQuantity).IsConcurrencyToken();
        modelBuilder.Entity<Medicine>().Property(medicine => medicine.Price).IsConcurrencyToken();
        modelBuilder.Entity<Medicine>().Property(medicine => medicine.IsActive).IsConcurrencyToken();
        modelBuilder.Entity<Medicine>().Property(medicine => medicine.PrescriptionRequired).IsConcurrencyToken();
        modelBuilder.Entity<CartItem>().Property(item => item.Quantity).IsConcurrencyToken();
        modelBuilder.Entity<CartItem>().Property(item => item.PrescriptionId).IsConcurrencyToken();

        modelBuilder.Entity<Medicine>()
            .Property(medicine => medicine.IsActive)
            .HasDefaultValue(true);

        modelBuilder.Entity<EventOutbox>()
            .HasIndex(entry => entry.EventId)
            .IsUnique();

        modelBuilder.Entity<StaffNotification>()
            .HasIndex(notification => notification.EventId)
            .IsUnique();

        modelBuilder.Entity<Stock>()
            .HasIndex(stock => new { stock.MedicineId, stock.BatchNumber })
            .IsUnique();

        modelBuilder.Entity<Prescription>()
            .HasOne(prescription => prescription.Medicine)
            .WithMany()
            .HasForeignKey(prescription => prescription.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Prescription>()
            .HasIndex(prescription => prescription.UploadedBy);

        modelBuilder.Entity<Cart>()
            .HasIndex(cart => cart.CustomerId)
            .IsUnique();

        modelBuilder.Entity<CartItem>()
            .HasOne(cartItem => cartItem.Cart)
            .WithMany(cart => cart.Items)
            .HasForeignKey(cartItem => cartItem.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CartItem>()
            .HasOne(cartItem => cartItem.Medicine)
            .WithMany()
            .HasForeignKey(cartItem => cartItem.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CartItem>()
            .HasOne(cartItem => cartItem.Prescription)
            .WithMany()
            .HasForeignKey(cartItem => cartItem.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CartItem>()
            .HasIndex(cartItem => cartItem.PrescriptionId);
    }
}
