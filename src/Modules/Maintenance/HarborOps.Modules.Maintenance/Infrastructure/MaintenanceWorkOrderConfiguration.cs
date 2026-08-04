using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class MaintenanceWorkOrderConfiguration : IEntityTypeConfiguration<MaintenanceWorkOrder>
{
    public void Configure(EntityTypeBuilder<MaintenanceWorkOrder> builder)
    {
        builder.ToTable(
            "MaintenanceWorkOrders",
            MaintenanceModule.Schema,
            table => table.IsTemporal());

        builder.HasKey(workOrder => workOrder.Id);

        builder.Property(workOrder => workOrder.Id)
            .ValueGeneratedNever();

        builder.Property(workOrder => workOrder.VesselId)
            .IsRequired();

        builder.Property(workOrder => workOrder.DryDockId);

        builder.Property(workOrder => workOrder.AssignedEngineerId)
            .IsRequired();

        builder.Property(workOrder => workOrder.Title)
            .HasMaxLength(MaintenanceWorkOrder.TitleMaxLength)
            .IsRequired();

        builder.Property(workOrder => workOrder.Description)
            .HasMaxLength(MaintenanceWorkOrder.DescriptionMaxLength);

        builder.Property(workOrder => workOrder.Priority)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(workOrder => workOrder.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(workOrder => workOrder.DueUtc)
            .IsRequired();

        builder.Property(workOrder => workOrder.CreatedUtc)
            .IsRequired();

        builder.Property(workOrder => workOrder.CompletedUtc);

        builder.HasOne<DryDock>()
            .WithMany()
            .HasForeignKey(workOrder => workOrder.DryDockId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
