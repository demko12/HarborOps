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
    }
}
