using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable(
            "PurchaseOrders",
            MaintenanceModule.Schema,
            table => table.IsTemporal());

        builder.HasKey(purchaseOrder => purchaseOrder.Id);

        builder.Property(purchaseOrder => purchaseOrder.Id)
            .ValueGeneratedNever();

        builder.Property(purchaseOrder => purchaseOrder.SupplierId)
            .IsRequired();

        builder.Property(purchaseOrder => purchaseOrder.OrderDate)
            .IsRequired();

        builder.Property(purchaseOrder => purchaseOrder.ExpectedDelivery);

        builder.Property(purchaseOrder => purchaseOrder.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(purchaseOrder => purchaseOrder.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
