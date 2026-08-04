using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewPositionConfiguration : IEntityTypeConfiguration<CrewPosition>
{
    public void Configure(EntityTypeBuilder<CrewPosition> builder)
    {
        builder.ToTable("CrewPositions", CrewModule.Schema);

        builder.HasKey(crewPosition => crewPosition.Id);

        builder.Property(crewPosition => crewPosition.Id)
            .ValueGeneratedNever();

        builder.Property(crewPosition => crewPosition.RankName)
            .HasMaxLength(CrewPosition.RankNameMaxLength)
            .IsRequired();

        builder.Property(crewPosition => crewPosition.Department)
            .HasMaxLength(CrewPosition.DepartmentMaxLength)
            .IsRequired();

        builder.Property(crewPosition => crewPosition.SeniorityLevel)
            .HasMaxLength(CrewPosition.SeniorityLevelMaxLength)
            .IsRequired();
    }
}
