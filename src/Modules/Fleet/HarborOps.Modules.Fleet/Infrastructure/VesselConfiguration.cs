using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Fleet.Infrastructure;

internal sealed class VesselConfiguration : IEntityTypeConfiguration<Vessel>
{
    public void Configure(EntityTypeBuilder<Vessel> builder)
    {
        builder.ToTable("Vessels", FleetModule.Schema);

        builder.HasKey(vessel => vessel.Id);

        builder.Property(vessel => vessel.Id)
            .ValueGeneratedNever();

        builder.Property(vessel => vessel.Name)
            .HasMaxLength(Vessel.NameMaxLength)
            .IsRequired();

        builder.Property(vessel => vessel.ImoNumber)
            .HasMaxLength(Vessel.ImoNumberLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(vessel => vessel.FlagState)
            .HasMaxLength(Vessel.FlagStateMaxLength)
            .IsRequired();

        builder.Property(vessel => vessel.VesselType)
            .HasMaxLength(Vessel.VesselTypeMaxLength)
            .IsRequired();

        builder.Property(vessel => vessel.CreatedUtc)
            .IsRequired();

        builder.HasIndex(vessel => vessel.ImoNumber)
            .IsUnique();
    }
}
