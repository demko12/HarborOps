using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class CargoDiscrepancyEventConfiguration
    : IEntityTypeConfiguration<CargoDiscrepancyEvent>
{
    public void Configure(EntityTypeBuilder<CargoDiscrepancyEvent> builder)
    {
        builder.ToTable("CargoDiscrepancyEvents", CargoModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.DiscrepancyId)
            .IsRequired();

        builder.Property(entry => entry.EventType)
            .HasConversion<string>()
            .HasMaxLength(CargoDiscrepancyEvent.EventTypeMaxLength)
            .IsRequired();

        builder.Property(entry => entry.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(CargoDiscrepancyEvent.StatusMaxLength);

        builder.Property(entry => entry.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(CargoDiscrepancyEvent.StatusMaxLength);

        builder.Property(entry => entry.Text)
            .HasMaxLength(CargoDiscrepancyEvent.TextMaxLength);

        builder.Property(entry => entry.ActorUserId)
            .IsRequired();

        builder.Property(entry => entry.ActorName)
            .HasMaxLength(CargoDiscrepancy.ActorNameMaxLength);

        builder.Property(entry => entry.OccurredAtUtc)
            .IsRequired();

        builder.HasIndex(entry => new { entry.DiscrepancyId, entry.OccurredAtUtc, entry.Id });

        builder.HasOne<CargoDiscrepancy>()
            .WithMany()
            .HasForeignKey(entry => entry.DiscrepancyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
