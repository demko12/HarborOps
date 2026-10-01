using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class PurchaseOrderClosureLineConfiguration : IEntityTypeConfiguration<PurchaseOrderClosureLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderClosureLine> builder)
    {
        builder.ToTable("PurchaseOrderClosureLines", MaintenanceModule.Schema);

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .ValueGeneratedNever();

        builder.Property(line => line.PurchaseOrderClosureId)
            .IsRequired();

        builder.Property(line => line.PurchaseOrderLineId)
            .IsRequired();

        builder.Property(line => line.InventoryItemId)
            .IsRequired();

        builder.Property(line => line.QuantityOrdered)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.ReceivedQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.ReturnedQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.ClosedQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.HasIndex(line => line.PurchaseOrderClosureId);

        // A purchase-order line is closed at most once.
        builder.HasIndex(line => line.PurchaseOrderLineId)
            .IsUnique();

        builder.HasIndex(line => line.InventoryItemId);

        builder.HasOne<PurchaseOrderClosure>()
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderClosureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PurchaseOrderLine>()
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(line => line.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
