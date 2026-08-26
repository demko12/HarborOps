using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewRotationConfiguration : IEntityTypeConfiguration<CrewRotation>
{
    public void Configure(EntityTypeBuilder<CrewRotation> builder)
    {
        builder.ToTable("CrewRotations", CrewModule.Schema);

        builder.HasKey(crewRotation => crewRotation.Id);

        builder.Property(crewRotation => crewRotation.Id)
            .ValueGeneratedNever();

        builder.Property(crewRotation => crewRotation.AssignmentId)
            .IsRequired();

        builder.Property(crewRotation => crewRotation.RelievingCrewMemberId);

        builder.Property(crewRotation => crewRotation.RotationPattern)
            .HasMaxLength(CrewRotation.RotationPatternMaxLength)
            .IsRequired();

        builder.Property(crewRotation => crewRotation.PlannedReliefDate)
            .IsRequired();

        builder.Property(crewRotation => crewRotation.Status)
            .HasMaxLength(CrewRotation.StatusMaxLength)
            .IsRequired();

        builder.Property(crewRotation => crewRotation.ExecutedAtUtc);

        builder.Property(crewRotation => crewRotation.ExecutedByUserId);

        builder.Property(crewRotation => crewRotation.ReplacementAssignmentId);

        builder.Property(crewRotation => crewRotation.CancelledAtUtc);

        builder.Property(crewRotation => crewRotation.CancelledByUserId);

        builder.Property(crewRotation => crewRotation.CancellationNotes)
            .HasMaxLength(CrewRotation.CancellationNotesMaxLength);
    }
}
