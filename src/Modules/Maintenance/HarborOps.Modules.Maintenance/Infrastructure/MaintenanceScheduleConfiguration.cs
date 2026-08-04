using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class MaintenanceScheduleConfiguration : IEntityTypeConfiguration<MaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<MaintenanceSchedule> builder)
    {
        builder.ToTable("MaintenanceSchedules", MaintenanceModule.Schema);

        builder.HasKey(schedule => schedule.Id);

        builder.Property(schedule => schedule.Id)
            .ValueGeneratedNever();

        builder.Property(schedule => schedule.MaintenanceType)
            .HasMaxLength(MaintenanceSchedule.MaintenanceTypeMaxLength)
            .IsRequired();

        builder.Property(schedule => schedule.Frequency)
            .HasMaxLength(MaintenanceSchedule.FrequencyMaxLength)
            .IsRequired();

        builder.Property(schedule => schedule.NextDueDate);

        builder.Property(schedule => schedule.NextDueHours);
    }
}
