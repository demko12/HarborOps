using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class DryDockConfiguration : IEntityTypeConfiguration<DryDock>
{
    public void Configure(EntityTypeBuilder<DryDock> builder)
    {
        builder.ToTable("DryDocks",
            MaintenanceModule.Schema,
            table => table.IsTemporal());

        builder.HasKey(dryDock => dryDock.Id);

        builder.Property(dryDock => dryDock.Id)
            .ValueGeneratedNever();

        builder.Property(dryDock => dryDock.VesselId)
            .IsRequired();

        builder.Property(dryDock => dryDock.PortId)
            .IsRequired();

        builder.Property(dryDock => dryDock.StartDate)
            .IsRequired();

        builder.Property(dryDock => dryDock.EndDate);

        builder.Property(dryDock => dryDock.PlannedTasks)
            .HasMaxLength(DryDock.PlannedTasksMaxLength)
            .IsRequired();

        builder.Property(dryDock => dryDock.ActualCost)
            .HasColumnType("decimal(18,2)");

        builder.Property(dryDock => dryDock.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
    }
}
