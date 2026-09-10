using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class VesselDefectConfiguration : IEntityTypeConfiguration<VesselDefect>
{
    public void Configure(EntityTypeBuilder<VesselDefect> builder)
    {
        builder.ToTable("VesselDefects", schema: MaintenanceModule.Schema, t => t.IsTemporal(h =>
        {
            h.HasPeriodStart("PeriodStart");
            h.HasPeriodEnd("PeriodEnd");
            h.UseHistoryTable("VesselDefectsHistory", schema: MaintenanceModule.Schema);
        }));

        builder.HasKey(defect => defect.Id);

        builder.Property(defect => defect.Id)
            .ValueGeneratedNever();

        builder.Property(defect => defect.VesselId)
            .IsRequired();

        builder.Property(defect => defect.EquipmentId);

        builder.Property(defect => defect.Title)
            .HasMaxLength(VesselDefect.TitleMaxLength)
            .IsRequired();

        builder.Property(defect => defect.Description)
            .HasMaxLength(VesselDefect.DescriptionMaxLength);

        builder.Property(defect => defect.ReportedSeverity)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(defect => defect.ObservedAtUtc)
            .IsRequired();

        builder.Property(defect => defect.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(defect => defect.ReportedByUserId)
            .IsRequired();

        builder.Property(defect => defect.ReportedAtUtc)
            .IsRequired();

        builder.Property(defect => defect.ConfirmedSeverity)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(defect => defect.Priority)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(defect => defect.TriageNotes)
            .HasMaxLength(VesselDefect.TriageNotesMaxLength);

        builder.Property(defect => defect.TriagedByUserId);

        builder.Property(defect => defect.TriagedAtUtc);

        builder.Property(defect => defect.RejectionReason)
            .HasMaxLength(VesselDefect.RejectionReasonMaxLength);

        builder.Property(defect => defect.RejectedByUserId);

        builder.Property(defect => defect.RejectedAtUtc);

        builder.Property(defect => defect.WorkOrderId);

        builder.Property(defect => defect.ConvertedByUserId);

        builder.Property(defect => defect.ConvertedAtUtc);

        builder.Property(defect => defect.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<Equipment>()
            .WithMany()
            .HasForeignKey(defect => defect.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MaintenanceWorkOrder>()
            .WithMany()
            .HasForeignKey(defect => defect.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
