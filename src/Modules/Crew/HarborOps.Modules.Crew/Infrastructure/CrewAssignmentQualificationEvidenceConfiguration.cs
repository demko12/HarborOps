using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewAssignmentQualificationEvidenceConfiguration : IEntityTypeConfiguration<CrewAssignmentQualificationEvidence>
{
    public void Configure(EntityTypeBuilder<CrewAssignmentQualificationEvidence> builder)
    {
        builder.ToTable("CrewAssignmentQualificationEvidences", CrewModule.Schema);

        builder.HasKey(evidence => evidence.Id);

        builder.Property(evidence => evidence.Id)
            .ValueGeneratedNever();

        builder.Property(evidence => evidence.AssignmentId)
            .IsRequired();

        builder.Property(evidence => evidence.PolicyId)
            .IsRequired();

        builder.Property(evidence => evidence.PolicyConcurrencyTokenAtDecision)
            .IsRequired();

        builder.Property(evidence => evidence.CertificateIdsSnapshot)
            .HasMaxLength(CrewAssignmentQualificationEvidence.CertificateIdsSnapshotMaxLength)
            .IsRequired();

        builder.Property(evidence => evidence.RecordedByUserId)
            .IsRequired();

        builder.Property(evidence => evidence.RecordedAtUtc)
            .IsRequired();

        builder.HasIndex(evidence => evidence.AssignmentId);
    }
}
