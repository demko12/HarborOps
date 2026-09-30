using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLines", MaintenanceModule.Schema);

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .ValueGeneratedNever();

        builder.Property(line => line.PurchaseOrderId)
            .IsRequired();

        builder.Property(line => line.PartId)
            .IsRequired();

        builder.Property(line => line.QuantityOrdered)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.UnitCost)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(line => line.ReceivedQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(line => line.LegacyReceivedQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(line => line.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
