using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class ContainerControlEventConfiguration
    : IEntityTypeConfiguration<ContainerControlEvent>
{
    public void Configure(EntityTypeBuilder<ContainerControlEvent> builder)
    {
        builder.ToTable("ContainerControlEvents", CargoModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.ContainerControlId)
            .IsRequired();

        builder.Property(entry => entry.Action)
            .HasConversion<string>()
            .HasMaxLength(ContainerControlEvent.ActionMaxLength)
            .IsRequired();

        builder.Property(entry => entry.FromState)
            .HasConversion<string>()
            .HasMaxLength(ContainerControlEvent.StateMaxLength)
            .IsRequired();

        builder.Property(entry => entry.ToState)
            .HasConversion<string>()
            .HasMaxLength(ContainerControlEvent.StateMaxLength)
            .IsRequired();

        builder.Property(entry => entry.SealNumber)
            .HasMaxLength(ContainerControl.SealNumberMaxLength)
            .IsUnicode(false);

        builder.Property(entry => entry.InspectionResult)
            .HasConversion<string>()
            .HasMaxLength(ContainerControlEvent.InspectionResultMaxLength);

        builder.Property(entry => entry.TemperatureCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(entry => entry.Notes)
            .HasMaxLength(ContainerControlEvent.NotesMaxLength);

        builder.Property(entry => entry.ActorUserId)
            .IsRequired();

        builder.Property(entry => entry.OccurredUtc)
            .IsRequired();

        builder.HasIndex(entry => new { entry.ContainerControlId, entry.OccurredUtc });

        builder.HasOne<ContainerControl>()
            .WithMany()
            .HasForeignKey(entry => entry.ContainerControlId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
