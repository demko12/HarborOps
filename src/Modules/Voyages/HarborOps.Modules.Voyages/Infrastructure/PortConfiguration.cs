using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortConfiguration : IEntityTypeConfiguration<Port>
{
    public void Configure(EntityTypeBuilder<Port> builder)
    {
        builder.ToTable("Ports", VoyagesModule.Schema);

        builder.HasKey(port => port.Id);

        builder.Property(port => port.Id)
            .ValueGeneratedNever();

        builder.Property(port => port.Name)
            .HasMaxLength(Port.NameMaxLength)
            .IsRequired();

        builder.Property(port => port.City)
            .HasMaxLength(Port.CityMaxLength);

        builder.Property(port => port.Country)
            .HasMaxLength(Port.CountryMaxLength)
            .IsRequired();

        builder.Property(port => port.UnLocode)
            .HasMaxLength(Port.UnLocodeLength)
            .IsUnicode(false);

        builder.Property(port => port.Latitude)
            .HasPrecision(9, 6);

        builder.Property(port => port.Longitude)
            .HasPrecision(9, 6);

        builder.Property(port => port.TimeZone)
            .HasMaxLength(Port.TimeZoneMaxLength);

        builder.Property(port => port.ShipyardName)
            .HasMaxLength(Port.ShipyardNameMaxLength);
    }
}
