using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class ScheduleOccurrenceConfiguration : IEntityTypeConfiguration<ScheduleOccurrence>
{
    public void Configure(EntityTypeBuilder<ScheduleOccurrence> builder)
    {
        builder.ToTable("ScheduleOccurrences", MaintenanceModule.Schema);

        builder.HasKey(occurrence => occurrence.Id);

        builder.Property(occurrence => occurrence.Id)
            .ValueGeneratedNever();

        builder.Property(occurrence => occurrence.ScheduleId)
            .IsRequired();

        builder.Property(occurrence => occurrence.DueDate)
            .IsRequired();

        builder.Property(occurrence => occurrence.State)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(occurrence => occurrence.WorkOrderId)
            .IsRequired();

        builder.Property(occurrence => occurrence.ReplacedWorkOrderId);

        builder.Property(occurrence => occurrence.GeneratedUtc)
            .IsRequired();

        builder.Property(occurrence => occurrence.ResolvedUtc);

        builder.HasIndex(occurrence => new
        {
            occurrence.ScheduleId,
            occurrence.DueDate,
        })
            .IsUnique();

        builder.HasIndex(occurrence => occurrence.WorkOrderId);

        builder.HasOne<MaintenanceSchedule>()
            .WithMany()
            .HasForeignKey(occurrence => occurrence.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MaintenanceWorkOrder>()
            .WithMany()
            .HasForeignKey(occurrence => occurrence.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
