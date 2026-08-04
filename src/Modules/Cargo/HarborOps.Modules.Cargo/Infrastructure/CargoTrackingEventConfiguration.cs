using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class CargoTrackingEventConfiguration
    : IEntityTypeConfiguration<CargoTrackingEvent>
{
    public void Configure(EntityTypeBuilder<CargoTrackingEvent> builder)
    {
        builder.ToTable("CargoTrackingEvents", CargoModule.Schema);

        builder.HasKey(trackingEvent => trackingEvent.Id);

        builder.Property(trackingEvent => trackingEvent.Id)
            .ValueGeneratedNever();

        builder.Property(trackingEvent => trackingEvent.CargoBookingId)
            .IsRequired();

        builder.Property(trackingEvent => trackingEvent.ResponsibleUserId)
            .IsRequired();

        builder.Property(trackingEvent => trackingEvent.Status)
            .HasConversion<string>()
            .HasMaxLength(CargoTrackingEvent.StatusMaxLength)
            .IsRequired();

        builder.Property(trackingEvent => trackingEvent.OccurredUtc)
            .IsRequired();

        builder.Property(trackingEvent => trackingEvent.Location)
            .HasMaxLength(CargoTrackingEvent.LocationMaxLength)
            .IsRequired();
    }
}
