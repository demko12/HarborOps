using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class VoyageScheduleChangeConfiguration
    : IEntityTypeConfiguration<VoyageScheduleChange>
{
    public void Configure(EntityTypeBuilder<VoyageScheduleChange> builder)
    {
        builder.ToTable("VoyageScheduleChanges", VoyagesModule.Schema);

        builder.HasKey(change => change.Id);

        builder.Property(change => change.Id)
            .ValueGeneratedNever();

        builder.Property(change => change.VoyageId)
            .IsRequired();

        builder.Property(change => change.ChangeType)
            .HasMaxLength(VoyageScheduleChange.ChangeTypeMaxLength)
            .IsRequired();

        builder.Property(change => change.Reason)
            .HasMaxLength(VoyageScheduleChange.ReasonMaxLength)
            .IsRequired();

        builder.Property(change => change.ChangedByUserId)
            .IsRequired();

        builder.Property(change => change.ChangedAtUtc)
            .IsRequired();

        builder.Property(change => change.PreviousStatus)
            .HasMaxLength(Voyage.StatusMaxLength)
            .IsRequired();

        builder.Property(change => change.NewStatus)
            .HasMaxLength(Voyage.StatusMaxLength)
            .IsRequired();

        builder.Property(change => change.PreviousPlannedDeparture)
            .IsRequired();

        builder.Property(change => change.NewPlannedDeparture)
            .IsRequired();

        builder.Property(change => change.PreviousPlannedArrival)
            .IsRequired();

        builder.Property(change => change.NewPlannedArrival)
            .IsRequired();

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(change => change.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(change => change.PreviousRouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(change => change.NewRouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(change => change.PreviousDeparturePortId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(change => change.NewDeparturePortId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(change => change.PreviousArrivalPortId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(change => change.NewArrivalPortId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(change => new { change.VoyageId, change.ChangedAtUtc });
    }
}
