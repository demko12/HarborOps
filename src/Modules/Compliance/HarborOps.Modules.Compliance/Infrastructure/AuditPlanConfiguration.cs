using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class AuditPlanConfiguration : IEntityTypeConfiguration<AuditPlan>
{
    public void Configure(EntityTypeBuilder<AuditPlan> builder)
    {
        builder.ToTable("AuditPlans", schema: ComplianceModule.Schema);

        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.Id)
            .ValueGeneratedNever();

        builder.Property(plan => plan.VesselId)
            .IsRequired();

        builder.Property(plan => plan.Type)
            .HasMaxLength(SurveyAudit.TypeMaxLength)
            .IsRequired();

        builder.Property(plan => plan.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(plan => plan.PlannedStartDate)
            .IsRequired();

        builder.Property(plan => plan.DueDate)
            .IsRequired();

        builder.Property(plan => plan.AssignedInspectorUserId);

        builder.Property(plan => plan.Scope)
            .HasMaxLength(AuditPlan.ScopeMaxLength)
            .IsRequired();

        builder.Property(plan => plan.Notes)
            .HasMaxLength(AuditPlan.NotesMaxLength);

        builder.Property(plan => plan.StartedAtUtc);

        builder.Property(plan => plan.CompletedAtUtc);

        builder.Property(plan => plan.CancelledAtUtc);

        builder.Property(plan => plan.CancellationReason)
            .HasMaxLength(AuditPlanHistory.ReasonMaxLength);

        builder.HasOne(plan => plan.Survey)
            .WithMany()
            .HasForeignKey(plan => plan.SurveyAuditId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(plan => new { plan.VesselId, plan.Type, plan.Status });
    }
}
