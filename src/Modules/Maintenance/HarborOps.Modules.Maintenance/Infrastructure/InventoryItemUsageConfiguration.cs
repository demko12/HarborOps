using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class InventoryItemUsageConfiguration : IEntityTypeConfiguration<InventoryItemUsage>
{
    public void Configure(EntityTypeBuilder<InventoryItemUsage> builder)
    {
        builder.ToTable("InventoryItemUsages", MaintenanceModule.Schema);

        builder.HasKey(usage => new
        {
            usage.WorkOrderId,
            usage.PartId,
        });

        builder.Property(usage => usage.WorkOrderId)
            .IsRequired();

        builder.Property(usage => usage.PartId)
            .IsRequired();

        builder.Property(usage => usage.QuantityUsed)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.HasOne<MaintenanceWorkOrder>()
            .WithMany()
            .HasForeignKey(usage => usage.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(usage => usage.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
