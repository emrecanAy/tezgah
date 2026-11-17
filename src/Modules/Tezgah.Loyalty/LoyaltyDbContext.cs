using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Tenancy;

namespace Tezgah.Loyalty;

internal sealed class LoyaltyDbContext(DbContextOptions<LoyaltyDbContext> options, TenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public DbSet<LoyaltyProgram> Programs => Set<LoyaltyProgram>();

    public DbSet<LoyaltyCard> Cards => Set<LoyaltyCard>();

    public DbSet<CardEntry> Entries => Set<CardEntry>();

    public DbSet<VoidedSource> VoidedSources => Set<VoidedSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(LoyaltyModule.Key);

        modelBuilder.Entity<LoyaltyProgram>(program =>
        {
            program.ToTable("programs");
            program.Property(p => p.Id).ValueGeneratedNever();
            program.Property(p => p.Name).HasMaxLength(100);
            program.Property(p => p.Reward).HasMaxLength(200);
            program.Property(p => p.EarnRule).HasConversion<string>().HasMaxLength(20);
            program.Property(p => p.Category).HasMaxLength(LoyaltyProgram.MaxCategoryLength);
        });

        modelBuilder.Entity<LoyaltyCard>(card =>
        {
            card.ToTable("cards");
            card.Property(c => c.Id).ValueGeneratedNever();
            card.HasOne<LoyaltyProgram>().WithMany().HasForeignKey(c => c.ProgramId);
            // One card per customer per program.
            card.HasIndex(c => new { c.ProgramId, c.CustomerId }).IsUnique();
        });

        modelBuilder.Entity<CardEntry>(entry =>
        {
            entry.ToTable("card_entries");
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
            entry.Property(e => e.Note).HasMaxLength(300);
            entry.Property(e => e.Source).HasMaxLength(100);
            entry.Property(e => e.CreatedBy).HasMaxLength(120);
            entry.HasOne<LoyaltyCard>().WithMany().HasForeignKey(e => e.CardId);
            entry.HasIndex(e => new { e.CardId, e.Kind, e.Source }).IsUnique().HasFilter("source IS NOT NULL");
            entry.HasIndex(e => e.Source).HasFilter("source IS NOT NULL");
            entry.HasIndex(e => new { e.TenantId, e.CreatedAt });
        });

        modelBuilder.Entity<VoidedSource>(voided =>
        {
            voided.ToTable("voided_sources");
            voided.Property(v => v.Id).ValueGeneratedNever();
            voided.Property(v => v.Source).HasMaxLength(100);
            voided.HasIndex(v => new { v.TenantId, v.Source }).IsUnique();
        });

        IsolateTenants(modelBuilder);
    }
}
