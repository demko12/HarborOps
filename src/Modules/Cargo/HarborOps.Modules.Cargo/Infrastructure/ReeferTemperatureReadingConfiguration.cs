using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class ReeferTemperatureReadingConfiguration
    : IEntityTypeConfiguration<ReeferTemperatureReading>
{
    public void Configure(EntityTypeBuilder<ReeferTemperatureReading> builder)
    {
        builder.ToTable("ReeferTemperatureReadings", CargoModule.Schema);

        builder.HasKey(reading => reading.Id);

        builder.Property(reading => reading.Id)
            .ValueGeneratedNever();

        builder.Property(reading => reading.ContainerId)
            .IsRequired();

        builder.Property(reading => reading.TemperatureCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(reading => reading.MeasuredAtUtc)
            .IsRequired();

        builder.Property(reading => reading.RecordedAtUtc)
            .IsRequired();

        builder.Property(reading => reading.Notes)
            .HasMaxLength(ReeferTemperatureReading.NotesMaxLength);

        builder.Property(reading => reading.RecordedByUserId)
            .IsRequired();

        builder.Property(reading => reading.ThresholdMinimumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(reading => reading.ThresholdMaximumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(reading => reading.ThresholdTargetCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(reading => reading.ThresholdProfileVersion)
            .IsRequired();

        builder.Property(reading => reading.Status)
            .HasConversion<string>()
            .HasMaxLength(ReeferTemperatureReading.StatusMaxLength)
            .IsRequired();

        builder.Property(reading => reading.CorrectionReason)
            .HasMaxLength(ReeferTemperatureReading.ReasonMaxLength);

        builder.Property(reading => reading.VoidReason)
            .HasMaxLength(ReeferTemperatureReading.ReasonMaxLength);

        builder.Property(reading => reading.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(reading => new
        {
            reading.ContainerId,
            reading.MeasuredAtUtc,
            reading.Id,
        });

        builder.HasIndex(reading => new { reading.ContainerId, reading.Status });

        builder.HasOne<Container>()
            .WithMany()
            .HasForeignKey(reading => reading.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReeferTemperatureReading>()
            .WithMany()
            .HasForeignKey(reading => reading.CorrectsReadingId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ReeferTemperatureReading>()
            .WithMany()
            .HasForeignKey(reading => reading.SupersededByReadingId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
