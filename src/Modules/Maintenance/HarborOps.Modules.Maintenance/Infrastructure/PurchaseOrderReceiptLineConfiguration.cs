using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class PurchaseOrderReceiptLineConfiguration : IEntityTypeConfiguration<PurchaseOrderReceiptLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceiptLine> builder)
    {
        builder.ToTable("PurchaseOrderReceiptLines", MaintenanceModule.Schema);

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .ValueGeneratedNever();

        builder.Property(line => line.PurchaseOrderReceiptId)
            .IsRequired();

        builder.Property(line => line.PurchaseOrderLineId)
            .IsRequired();

        builder.Property(line => line.InventoryItemId)
            .IsRequired();

        builder.Property(line => line.Quantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.LineReceivedQuantityAfter)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        // A receipt lists each purchase-order line at most once.
        builder.HasIndex(line => new
        {
            line.PurchaseOrderReceiptId,
            line.PurchaseOrderLineId,
        })
            .IsUnique();

        builder.HasIndex(line => line.PurchaseOrderLineId);

        builder.HasIndex(line => line.InventoryItemId);

        builder.HasOne<PurchaseOrderReceipt>()
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderReceiptId)
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
