using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Fleet.Infrastructure;

internal sealed class VesselTypeConfiguration : IEntityTypeConfiguration<VesselType>
{
    public void Configure(EntityTypeBuilder<VesselType> builder)
    {
        builder.ToTable("VesselTypes", FleetModule.Schema);

        builder.HasKey(vesselType => vesselType.Id);

        builder.Property(vesselType => vesselType.Id)
            .ValueGeneratedNever();

        builder.Property(vesselType => vesselType.Name)
            .HasMaxLength(VesselType.NameMaxLength)
            .IsRequired();

        builder.Property(vesselType => vesselType.Code)
            .HasMaxLength(VesselType.CodeMaxLength)
            .IsRequired();

        builder.Property(vesselType => vesselType.Description)
            .HasMaxLength(VesselType.DescriptionMaxLength)
            .IsRequired();

        builder.HasIndex(vesselType => vesselType.Code)
            .IsUnique();
    }
}
