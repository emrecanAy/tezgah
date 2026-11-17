using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Messaging;
using Tezgah.Core.Tenancy;

namespace Tezgah.Orders;

internal sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public DbSet<MenuItem> Menu => Set<MenuItem>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(OrdersModule.Key).AddOutbox();

        modelBuilder.Entity<MenuItem>(item =>
        {
            item.ToTable("menu_items");
            item.Property(i => i.Id).ValueGeneratedNever();
            item.Property(i => i.Name).HasMaxLength(MenuItem.MaxNameLength);
            item.Property(i => i.Category).HasMaxLength(MenuItem.MaxCategoryLength);
            item.Property(i => i.Price).HasPrecision(10, 2);
            item.HasIndex(i => new { i.TenantId, i.Name }).IsUnique();
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.Property(o => o.Id).ValueGeneratedNever();
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.Subtotal).HasPrecision(12, 2);
            order.Property(o => o.Discount).HasPrecision(12, 2);
            order.Property(o => o.Total).HasPrecision(12, 2);
            order.Property(o => o.RewardNote).HasMaxLength(300);
            order.Property(o => o.PlacedBy).HasMaxLength(120);
            order.Property(o => o.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
            order.HasIndex(o => new { o.TenantId, o.PlacedAt });

            order.OwnsMany(o => o.Lines, line =>
            {
                line.ToTable("order_lines");
                line.WithOwner().HasForeignKey("OrderId");
                line.HasKey("OrderId", nameof(OrderLine.Position));
                line.Property(l => l.Position).ValueGeneratedNever();
                line.Property(l => l.Name).HasMaxLength(MenuItem.MaxNameLength);
                line.Property(l => l.Category).HasMaxLength(MenuItem.MaxCategoryLength);
                line.Property(l => l.UnitPrice).HasPrecision(10, 2);
                line.Ignore(l => l.Amount);
            });
            order.Navigation(o => o.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        IsolateTenants(modelBuilder);
    }
}
