using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class MaintenanceRecordConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> builder)
    {
        builder.ToTable("MaintenanceRecords", MaintenanceModule.Schema);

        builder.HasKey(record => record.Id);

        builder.Property(record => record.Id)
            .ValueGeneratedNever();

        builder.Property(record => record.WorkOrderId)
            .IsRequired();

        builder.Property(record => record.TechnicianId)
            .IsRequired();

        builder.Property(record => record.CompletionDate)
            .IsRequired();

        builder.Property(record => record.Notes)
            .HasMaxLength(MaintenanceRecord.NotesMaxLength);

        builder.Property(record => record.Cost)
            .HasColumnType("decimal(18,2)");

        builder.Property(record => record.LaborHours)
            .HasColumnType("decimal(9,2)");

        builder.HasOne<MaintenanceWorkOrder>()
            .WithMany()
            .HasForeignKey(record => record.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
