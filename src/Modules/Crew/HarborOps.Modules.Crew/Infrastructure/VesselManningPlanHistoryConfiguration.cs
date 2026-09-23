using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class VesselManningPlanHistoryConfiguration : IEntityTypeConfiguration<VesselManningPlanHistory>
{
    public void Configure(EntityTypeBuilder<VesselManningPlanHistory> builder)
    {
        builder.ToTable("VesselManningPlanHistories", CrewModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.VesselManningPlanId)
            .IsRequired();

        builder.Property(entry => entry.EventType)
            .HasMaxLength(VesselManningPlanHistory.EventTypeMaxLength)
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasMaxLength(VesselManningPlanHistory.ReasonMaxLength);

        builder.Property(entry => entry.ActorUserId)
            .IsRequired();

        builder.Property(entry => entry.OccurredAtUtc)
            .IsRequired();

        builder.Property(entry => entry.PreviousEffectiveEndDate);

        builder.Property(entry => entry.NewEffectiveEndDate);

        builder.Property(entry => entry.PreviousLinesSnapshot)
            .HasMaxLength(VesselManningPlanHistory.LinesSnapshotMaxLength);

        builder.Property(entry => entry.NewLinesSnapshot)
            .HasMaxLength(VesselManningPlanHistory.LinesSnapshotMaxLength);

        builder.HasIndex(entry => new { entry.VesselManningPlanId, entry.OccurredAtUtc });
    }
}
