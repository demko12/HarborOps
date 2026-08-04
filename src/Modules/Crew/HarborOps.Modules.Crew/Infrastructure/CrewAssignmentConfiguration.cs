using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewAssignmentConfiguration : IEntityTypeConfiguration<CrewAssignment>
{
    public void Configure(EntityTypeBuilder<CrewAssignment> builder)
    {
        builder.ToTable("CrewAssignments", CrewModule.Schema);

        builder.HasKey(crewAssignment => crewAssignment.Id);

        builder.Property(crewAssignment => crewAssignment.Id)
            .ValueGeneratedNever();

        builder.Property(crewAssignment => crewAssignment.CrewMemberId)
            .IsRequired();

        builder.Property(crewAssignment => crewAssignment.VesselId)
            .IsRequired();

        builder.Property(crewAssignment => crewAssignment.RankId)
            .IsRequired();

        builder.Property(crewAssignment => crewAssignment.SignOnPortId);

        builder.Property(crewAssignment => crewAssignment.SignOffPortId);

        builder.Property(crewAssignment => crewAssignment.SignOnDate)
            .IsRequired();

        builder.Property(crewAssignment => crewAssignment.PlannedSignOffDate)
            .IsRequired();

        builder.Property(crewAssignment => crewAssignment.ActualSignOffDate);

        builder.Property(crewAssignment => crewAssignment.Status)
            .HasMaxLength(CrewAssignment.StatusMaxLength)
            .IsRequired();
    }
}
