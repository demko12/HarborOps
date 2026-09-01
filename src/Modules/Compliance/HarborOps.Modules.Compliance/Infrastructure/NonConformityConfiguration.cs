using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class NonConformityConfiguration : IEntityTypeConfiguration<NonConformity>
{
    public void Configure(EntityTypeBuilder<NonConformity> builder)
    {
        builder.ToTable("NonConformities", schema: ComplianceModule.Schema, t => t.IsTemporal(h =>
        {
            h.HasPeriodStart("PeriodStart");
            h.HasPeriodEnd("PeriodEnd");
            h.UseHistoryTable("NonConformitiesHistory", schema: ComplianceModule.Schema);
        })); ;

        builder.HasKey(n => n.Id);

        builder.Property(n => n.EvidenceDocumentId)
            .IsRequired();

        builder.Property(n => n.Severity)
            .HasMaxLength(NonConformity.SeverityMaxLength)
            .IsRequired();

        builder.Property(n => n.CorrectiveActionDeadline)
            .IsRequired();

        builder.Property(n => n.ClosureStatus)
            .HasMaxLength(NonConformity.ClosureStatusMaxLength)
            .IsRequired();

        builder.Property(n => n.ClosureNotes)
            .HasMaxLength(NonConformity.ClosureNotesMaxLength);

        builder.Property(n => n.ClosedAtUtc);

        builder.Property(n => n.ClosedByUserId);

        builder.Property(n => n.FinalEvidenceDocumentId);

        builder.HasOne(n => n.Survey)
            .WithMany()
            .HasForeignKey(n => n.SurveyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Psc)
            .WithMany()
            .HasForeignKey(n => n.PscId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => n.EvidenceDocumentId);
    }
}
