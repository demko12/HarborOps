using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class PscDetentionDecisionConfiguration : IEntityTypeConfiguration<PscDetentionDecision>
{
    public void Configure(EntityTypeBuilder<PscDetentionDecision> builder)
    {
        builder.ToTable("PscDetentionDecisions", schema: ComplianceModule.Schema);
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.Id).ValueGeneratedNever();
        builder.Property(decision => decision.PscInspectionId).IsRequired();
        builder.Property(decision => decision.Decision).HasMaxLength(PscDetentionDecision.DecisionMaxLength).IsRequired();
        builder.Property(decision => decision.AuthorityReference).HasMaxLength(PscDetentionDecision.AuthorityReferenceMaxLength).IsRequired();
        builder.Property(decision => decision.Reason).HasMaxLength(PscDetentionDecision.ReasonMaxLength);
        builder.Property(decision => decision.ActorUserId).IsRequired();
        builder.Property(decision => decision.OccurredAtUtc).IsRequired();
        builder.HasOne<PscInspection>().WithMany().HasForeignKey(decision => decision.PscInspectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(decision => new { decision.PscInspectionId, decision.OccurredAtUtc });
    }
}
