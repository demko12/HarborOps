using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Fleet.Infrastructure;

internal sealed class FleetAssignmentConfiguration : IEntityTypeConfiguration<FleetAssignment>
{
    public void Configure(EntityTypeBuilder<FleetAssignment> builder)
    {
        builder.ToTable("FleetAssignments", FleetModule.Schema);

        builder.HasKey(fleetAssignment => fleetAssignment.Id);

        builder.Property(fleetAssignment => fleetAssignment.Id)
            .ValueGeneratedNever();

        builder.Property(fleetAssignment => fleetAssignment.VesselId)
            .IsRequired();

        builder.Property(fleetAssignment => fleetAssignment.FleetId)
            .IsRequired();

        builder.Property(fleetAssignment => fleetAssignment.AssignedFromUtc)
            .IsRequired();

        builder.Property(fleetAssignment => fleetAssignment.IsActive)
            .IsRequired();

        builder.HasIndex(fleetAssignment => fleetAssignment.VesselId);

        builder.HasIndex(fleetAssignment => fleetAssignment.FleetId);
    }
}
