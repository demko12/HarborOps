using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortCallClearanceEventConfiguration
    : IEntityTypeConfiguration<PortCallClearanceEvent>
{
    public void Configure(EntityTypeBuilder<PortCallClearanceEvent> builder)
    {
        builder.ToTable("PortCallClearanceEvents", VoyagesModule.Schema);

        builder.HasKey(clearanceEvent => clearanceEvent.Id);

        builder.Property(clearanceEvent => clearanceEvent.Id)
            .ValueGeneratedNever();

        builder.Property(clearanceEvent => clearanceEvent.PortCallClearanceId)
            .IsRequired();

        builder.Property(clearanceEvent => clearanceEvent.Sequence)
            .IsRequired();

        builder.Property(clearanceEvent => clearanceEvent.EventType)
            .HasMaxLength(PortCallClearanceEvent.EventTypeMaxLength)
            .IsRequired();

        builder.Property(clearanceEvent => clearanceEvent.Reason)
            .HasMaxLength(PortCallClearanceEvent.ReasonMaxLength);

        builder.Property(clearanceEvent => clearanceEvent.ActorUserId)
            .IsRequired();

        builder.Property(clearanceEvent => clearanceEvent.OccurredAtUtc)
            .IsRequired();

        builder.HasOne<PortCallClearance>()
            .WithMany()
            .HasForeignKey(clearanceEvent => clearanceEvent.PortCallClearanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PortCallClearanceItem>()
            .WithMany()
            .HasForeignKey(clearanceEvent => clearanceEvent.PortCallClearanceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(clearanceEvent => new
        {
            clearanceEvent.PortCallClearanceId,
            clearanceEvent.Sequence,
        }).IsUnique();
    }
}
