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

        builder.Property(vessel => vessel.CompanyId)
            .IsRequired();

        builder.Property(vessel => vessel.VesselTypeId)
            .IsRequired();

        builder.Property(vessel => vessel.Name)
            .HasMaxLength(Vessel.NameMaxLength)
            .IsRequired();

        builder.Property(vessel => vessel.ImoNumber)
            .HasMaxLength(Vessel.ImoNumberLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(vessel => vessel.Mmsi)
            .HasMaxLength(Vessel.MmsiMaxLength);

        builder.Property(vessel => vessel.CallSign)
            .HasMaxLength(Vessel.CallSignMaxLength);

        builder.Property(vessel => vessel.FlagState)
            .HasMaxLength(Vessel.FlagStateMaxLength)
            .IsRequired();

        builder.Property(vessel => vessel.PortOfRegistry)
            .HasMaxLength(Vessel.PortOfRegistryMaxLength);

        builder.Property(vessel => vessel.ClassificationSociety)
            .HasMaxLength(Vessel.ClassificationSocietyMaxLength);

        builder.Property(vessel => vessel.Dwt)
            .HasPrecision(18, 2);

        builder.Property(vessel => vessel.Gt)
            .HasPrecision(18, 2);

        builder.Property(vessel => vessel.Loa)
            .HasPrecision(18, 2);

        builder.Property(vessel => vessel.Beam)
            .HasPrecision(18, 2);

        builder.Property(vessel => vessel.Draft)
            .HasPrecision(18, 2);

        builder.Property(vessel => vessel.CurrentLatitude)
            .HasPrecision(9, 6);

        builder.Property(vessel => vessel.CurrentLongitude)
            .HasPrecision(9, 6);

        builder.Property(vessel => vessel.LifecycleStage)
            .HasMaxLength(Vessel.LifecycleStageMaxLength);

        builder.Property(vessel => vessel.CurrentStatus)
            .HasMaxLength(Vessel.CurrentStatusMaxLength)
            .IsRequired();

        builder.Property(vessel => vessel.MsmdId)
            .HasMaxLength(Vessel.MsmdIdMaxLength);

        builder.Property(vessel => vessel.CreatedAtUtc)
            .IsRequired();

        builder.Property(vessel => vessel.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(vessel => vessel.ImoNumber)
            .IsUnique();
    }
}
