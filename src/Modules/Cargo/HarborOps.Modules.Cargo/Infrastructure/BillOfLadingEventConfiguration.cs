using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class BillOfLadingEventConfiguration
    : IEntityTypeConfiguration<BillOfLadingEvent>
{
    public void Configure(EntityTypeBuilder<BillOfLadingEvent> builder)
    {
        builder.ToTable("BillOfLadingEvents", CargoModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.BillOfLadingId)
            .IsRequired();

        builder.Property(entry => entry.Action)
            .HasConversion<string>()
            .HasMaxLength(BillOfLadingEvent.ActionMaxLength)
            .IsRequired();

        builder.Property(entry => entry.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(BillOfLadingEvent.StatusMaxLength)
            .IsRequired();

        builder.Property(entry => entry.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(BillOfLadingEvent.StatusMaxLength)
            .IsRequired();

        builder.Property(entry => entry.Notes)
            .HasMaxLength(BillOfLadingEvent.NotesMaxLength);

        builder.Property(entry => entry.ActorUserId)
            .IsRequired();

        builder.Property(entry => entry.OccurredUtc)
            .IsRequired();

        builder.HasIndex(entry => new { entry.BillOfLadingId, entry.OccurredUtc });

        builder.HasOne<BillOfLading>()
            .WithMany()
            .HasForeignKey(entry => entry.BillOfLadingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
