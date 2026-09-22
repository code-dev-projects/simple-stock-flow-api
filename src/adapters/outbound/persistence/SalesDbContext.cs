using Microsoft.EntityFrameworkCore;
using SimpleStockFlow.Domain.Catalog;
using SimpleStockFlow.Domain.Identity;
using SimpleStockFlow.Domain.Sales;

namespace SimpleStockFlow.Adapters.Persistence;

public sealed class SalesDbContext : DbContext
{
    public SalesDbContext(DbContextOptions<SalesDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sales");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
