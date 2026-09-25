using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class StocktakeLineConfiguration : IEntityTypeConfiguration<StocktakeLine>
{
    public void Configure(EntityTypeBuilder<StocktakeLine> builder)
    {
        builder.ToTable("StocktakeLines", MaintenanceModule.Schema);

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .ValueGeneratedNever();

        builder.Property(line => line.StocktakeId)
            .IsRequired();

        builder.Property(line => line.InventoryItemId)
            .IsRequired();

        builder.Property(line => line.BaselineQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.BaselineStockConcurrencyToken)
            .IsRequired();

        builder.Property(line => line.BaselineCapturedUtc)
            .IsRequired();

        builder.Property(line => line.CountedQuantity)
            .HasColumnType("decimal(18,3)");

        builder.Property(line => line.Variance)
            .HasColumnType("decimal(18,3)");

        builder.Property(line => line.VarianceReason)
            .HasMaxLength(Stocktake.ReasonMaxLength);

        builder.Property(line => line.CountedByUserId);

        builder.Property(line => line.CountedUtc);

        builder.HasIndex(line => new
        {
            line.StocktakeId,
            line.InventoryItemId,
        })
            .IsUnique();

        builder.HasIndex(line => line.InventoryItemId);

        builder.HasOne<Stocktake>()
            .WithMany()
            .HasForeignKey(line => line.StocktakeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(line => line.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
