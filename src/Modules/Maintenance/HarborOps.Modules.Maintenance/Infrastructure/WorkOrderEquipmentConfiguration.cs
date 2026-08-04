using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class WorkOrderEquipmentConfiguration : IEntityTypeConfiguration<WorkOrderEquipment>
{
    public void Configure(EntityTypeBuilder<WorkOrderEquipment> builder)
    {
        builder.ToTable("WorkOrderEquipment", MaintenanceModule.Schema);

        builder.HasKey(workOrderEquipment => new
        {
            workOrderEquipment.WorkOrderId,
            workOrderEquipment.EquipmentId,
        });

        builder.Property(workOrderEquipment => workOrderEquipment.WorkOrderId)
            .IsRequired();

        builder.Property(workOrderEquipment => workOrderEquipment.EquipmentId)
            .IsRequired();

        builder.HasOne<MaintenanceWorkOrder>()
            .WithMany()
            .HasForeignKey(workOrderEquipment => workOrderEquipment.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Equipment>()
            .WithMany()
            .HasForeignKey(workOrderEquipment => workOrderEquipment.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
