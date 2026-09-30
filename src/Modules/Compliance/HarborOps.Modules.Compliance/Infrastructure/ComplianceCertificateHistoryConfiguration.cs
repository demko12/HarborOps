using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class ComplianceCertificateHistoryConfiguration : IEntityTypeConfiguration<ComplianceCertificateHistory>
{
    public void Configure(EntityTypeBuilder<ComplianceCertificateHistory> builder)
    {
        builder.ToTable("ComplianceCertificateHistories", schema: ComplianceModule.Schema);

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .ValueGeneratedNever();

        builder.Property(history => history.CertificateId)
            .IsRequired();

        builder.Property(history => history.EventType)
            .HasMaxLength(ComplianceCertificateHistory.EventTypeMaxLength)
            .IsRequired();

        builder.Property(history => history.ActorUserId)
            .IsRequired();

        builder.Property(history => history.OccurredAtUtc)
            .IsRequired();

        builder.Property(history => history.Reason)
            .HasMaxLength(ComplianceCertificate.RevocationReasonMaxLength);

        builder.Property(history => history.SuccessorCertificateId);

        builder.HasOne<ComplianceCertificate>()
            .WithMany()
            .HasForeignKey(history => history.CertificateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(history => new { history.CertificateId, history.OccurredAtUtc });
    }
}
