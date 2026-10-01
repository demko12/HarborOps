using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class CargoBookingConfiguration : IEntityTypeConfiguration<CargoBooking>
{
    public void Configure(EntityTypeBuilder<CargoBooking> builder)
    {
        builder.ToTable("CargoBookings", CargoModule.Schema, table => table.IsTemporal());

        builder.HasKey(booking => booking.Id);

        builder.Property(booking => booking.Id)
            .ValueGeneratedNever();

        builder.Property(booking => booking.BookingNumber)
            .HasMaxLength(CargoBooking.BookingNumberMaxLength)
            .IsRequired();

        builder.Property(booking => booking.VoyageId)
            .IsRequired();

        builder.Property(booking => booking.Description)
            .HasMaxLength(CargoBooking.DescriptionMaxLength)
            .IsRequired();

        builder.Property(booking => booking.CargoType)
            .HasMaxLength(CargoBooking.CargoTypeMaxLength)
            .IsRequired();

        builder.Property(booking => booking.WeightKg)
            .HasPrecision(CargoBooking.WeightKgPrecision, CargoBooking.WeightKgScale)
            .IsRequired();

        builder.Property(booking => booking.PackageCount)
            .IsRequired();

        builder.Property(booking => booking.IsHazardous)
            .IsRequired();

        builder.Property(booking => booking.Status)
            .HasConversion<string>()
            .HasMaxLength(CargoBooking.StatusMaxLength)
            .IsRequired();

        builder.Property(booking => booking.CreatedUtc)
            .IsRequired();

        builder.Property(booking => booking.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(booking => booking.BookingNumber)
            .IsUnique();
    }
}
