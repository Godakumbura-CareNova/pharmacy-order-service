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

    public DbSet<Prescription> Prescriptions { get; set; }
}