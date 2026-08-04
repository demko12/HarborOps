using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class VoyageConfiguration : IEntityTypeConfiguration<Voyage>
{
    public void Configure(EntityTypeBuilder<Voyage> builder)
    {
        builder.ToTable("Voyages", VoyagesModule.Schema, table => table.IsTemporal());

        builder.HasKey(voyage => voyage.Id);

        builder.Property(voyage => voyage.Id)
            .ValueGeneratedNever();

        builder.Property(voyage => voyage.VesselId)
            .IsRequired();

        builder.Property(voyage => voyage.CaptainId);

        builder.Property(voyage => voyage.VoyageNumber)
            .HasMaxLength(Voyage.VoyageNumberMaxLength)
            .IsRequired();

        builder.Property(voyage => voyage.Status)
            .HasMaxLength(Voyage.StatusMaxLength)
            .IsRequired();

        builder.Property(voyage => voyage.PlannedDeparture)
            .IsRequired();

        builder.Property(voyage => voyage.PlannedArrival)
            .IsRequired();

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(voyage => voyage.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(voyage => voyage.DeparturePortId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(voyage => voyage.ArrivalPortId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
