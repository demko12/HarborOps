using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewQualificationPolicyHistoryConfiguration : IEntityTypeConfiguration<CrewQualificationPolicyHistory>
{
    public void Configure(EntityTypeBuilder<CrewQualificationPolicyHistory> builder)
    {
        builder.ToTable("CrewQualificationPolicyHistories", CrewModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.PolicyId)
            .IsRequired();

        builder.Property(entry => entry.EventType)
            .HasMaxLength(CrewQualificationPolicyHistory.EventTypeMaxLength)
            .IsRequired();

        builder.Property(entry => entry.ActorUserId)
            .IsRequired();

        builder.Property(entry => entry.OccurredAtUtc)
            .IsRequired();

        builder.Property(entry => entry.PreviousEnabled);

        builder.Property(entry => entry.NewEnabled);

        builder.Property(entry => entry.PreviousRequiredTypesSnapshot)
            .HasMaxLength(CrewQualificationPolicyHistory.RequiredTypesSnapshotMaxLength);

        builder.Property(entry => entry.NewRequiredTypesSnapshot)
            .HasMaxLength(CrewQualificationPolicyHistory.RequiredTypesSnapshotMaxLength);

        builder.HasIndex(entry => new { entry.PolicyId, entry.OccurredAtUtc });
    }
}
