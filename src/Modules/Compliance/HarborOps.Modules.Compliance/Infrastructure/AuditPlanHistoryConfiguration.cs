using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class AuditPlanHistoryConfiguration : IEntityTypeConfiguration<AuditPlanHistory>
{
    public void Configure(EntityTypeBuilder<AuditPlanHistory> builder)
    {
        builder.ToTable("AuditPlanHistories", schema: ComplianceModule.Schema);

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .ValueGeneratedNever();

        builder.Property(history => history.AuditPlanId)
            .IsRequired();

        builder.Property(history => history.EventType)
            .HasMaxLength(AuditPlanHistory.EventTypeMaxLength)
            .IsRequired();

        builder.Property(history => history.Reason)
            .HasMaxLength(AuditPlanHistory.ReasonMaxLength);

        builder.Property(history => history.ActorUserId)
            .IsRequired();

        builder.Property(history => history.OccurredAtUtc)
            .IsRequired();

        builder.Property(history => history.PreviousStatus)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(history => history.NewStatus)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(history => history.PreviousPlannedStartDate);

        builder.Property(history => history.NewPlannedStartDate);

        builder.Property(history => history.PreviousDueDate);

        builder.Property(history => history.NewDueDate);

        builder.Property(history => history.PreviousInspectorUserId);

        builder.Property(history => history.NewInspectorUserId);

        builder.HasOne<AuditPlan>()
            .WithMany()
            .HasForeignKey(history => history.AuditPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(history => new { history.AuditPlanId, history.OccurredAtUtc });
    }
}
