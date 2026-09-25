using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("InventoryAdjustments", MaintenanceModule.Schema);

        builder.HasKey(adjustment => adjustment.Id);

        builder.Property(adjustment => adjustment.Id)
            .ValueGeneratedNever();

        builder.Property(adjustment => adjustment.QuantityBefore)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(adjustment => adjustment.CountedQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(adjustment => adjustment.QuantityDelta)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(adjustment => adjustment.Reason)
            .HasMaxLength(Stocktake.ReasonMaxLength)
            .IsRequired();

        builder.Property(adjustment => adjustment.ActorUserId)
            .IsRequired();

        builder.Property(adjustment => adjustment.AdjustedUtc)
            .IsRequired();

        // A stocktake adjusts each item at most once, even when a post is retried.
        builder.HasIndex(adjustment => new
        {
            adjustment.StocktakeId,
            adjustment.InventoryItemId,
        })
            .IsUnique();

        builder.HasIndex(adjustment => new
        {
            adjustment.InventoryItemId,
            adjustment.AdjustedUtc,
        });

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Stocktake>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.StocktakeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
