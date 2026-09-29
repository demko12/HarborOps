using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewCertificateHistoryConfiguration : IEntityTypeConfiguration<CrewCertificateHistory>
{
    public void Configure(EntityTypeBuilder<CrewCertificateHistory> builder)
    {
        builder.ToTable("CrewCertificateHistories", CrewModule.Schema);

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .ValueGeneratedNever();

        builder.Property(history => history.CertificateId)
            .IsRequired();

        builder.Property(history => history.EventType)
            .HasMaxLength(CrewCertificateHistory.EventTypeMaxLength)
            .IsRequired();

        builder.Property(history => history.ActorUserId)
            .IsRequired();

        builder.Property(history => history.OccurredAtUtc)
            .IsRequired();

        builder.Property(history => history.Reason)
            .HasMaxLength(CrewCertificateHistory.ReasonMaxLength);

        builder.HasIndex(history => history.CertificateId);
    }
}
