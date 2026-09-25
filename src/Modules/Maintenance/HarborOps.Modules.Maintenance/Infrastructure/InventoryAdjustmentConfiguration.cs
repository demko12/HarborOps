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

        // The column keeps its stocktake-era name so existing history and deployed code stay compatible.
        builder.Property(adjustment => adjustment.QuantityAfter)
            .HasColumnName("CountedQuantity")
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
            .IsUnique()
            .HasFilter("[StocktakeId] IS NOT NULL");

        // A supplier return line moves stock at most once, even when a post is retried.
        builder.HasIndex(adjustment => adjustment.SupplierReturnLineId)
            .IsUnique()
            .HasFilter("[SupplierReturnLineId] IS NOT NULL");

        builder.HasIndex(adjustment => adjustment.SupplierReturnId);

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

        builder.HasOne<SupplierReturn>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.SupplierReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SupplierReturnLine>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.SupplierReturnLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
