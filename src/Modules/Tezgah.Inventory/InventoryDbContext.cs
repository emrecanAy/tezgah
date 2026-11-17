using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Tenancy;

namespace Tezgah.Inventory;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockMovement> Movements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(InventoryModule.Key);

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products", table => table.HasCheckConstraint("ck_products_stock_not_negative", "stock >= 0"));
            product.Property(p => p.Id).ValueGeneratedNever();
            product.Property(p => p.Name).HasMaxLength(120);
            product.Property(p => p.Unit).HasMaxLength(12);
            product.Property(p => p.Stock).HasPrecision(12, 3);
            product.Property(p => p.ReorderLevel).HasPrecision(12, 3);
            // Written on insert only; see Product.Stock.
            product.Property(p => p.Stock).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            product.HasIndex(p => new { p.TenantId, p.Name }).IsUnique();
        });

        modelBuilder.Entity<StockMovement>(movement =>
        {
            movement.ToTable("stock_movements");
            movement.Property(m => m.Id).ValueGeneratedNever();
            movement.Property(m => m.Kind).HasConversion<string>().HasMaxLength(10);
            movement.Property(m => m.Quantity).HasPrecision(12, 3);
            movement.Property(m => m.Note).HasMaxLength(300);
            movement.Property(m => m.CreatedBy).HasMaxLength(120);
            movement.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId);
            movement.HasIndex(m => new { m.ProductId, m.CreatedAt });
        });

        IsolateTenants(modelBuilder);
    }
}
