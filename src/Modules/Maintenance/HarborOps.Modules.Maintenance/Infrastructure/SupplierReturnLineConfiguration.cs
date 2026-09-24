using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class SupplierReturnLineConfiguration : IEntityTypeConfiguration<SupplierReturnLine>
{
    public void Configure(EntityTypeBuilder<SupplierReturnLine> builder)
    {
        builder.ToTable("SupplierReturnLines", MaintenanceModule.Schema);

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .ValueGeneratedNever();

        builder.Property(line => line.SupplierReturnId)
            .IsRequired();

        builder.Property(line => line.PurchaseOrderLineId)
            .IsRequired();

        builder.Property(line => line.InventoryItemId)
            .IsRequired();

        builder.Property(line => line.Quantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.Reason)
            .HasMaxLength(SupplierReturn.ReasonMaxLength)
            .IsRequired();

        // A return lists each purchase-order line at most once.
        builder.HasIndex(line => new
        {
            line.SupplierReturnId,
            line.PurchaseOrderLineId,
        })
            .IsUnique();

        builder.HasIndex(line => line.PurchaseOrderLineId);

        builder.HasIndex(line => line.InventoryItemId);

        builder.HasOne<SupplierReturn>()
            .WithMany()
            .HasForeignKey(line => line.SupplierReturnId)
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
