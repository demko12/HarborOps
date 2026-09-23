using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class ReeferExcursionConfiguration : IEntityTypeConfiguration<ReeferExcursion>
{
    private const string OpenExcursionIndexName = "IX_ReeferExcursions_OpenContainerId";

    private const string OpenExcursionFilter = "[Status] = 'Open'";

    public void Configure(EntityTypeBuilder<ReeferExcursion> builder)
    {
        builder.ToTable("ReeferExcursions", CargoModule.Schema);

        builder.HasKey(excursion => excursion.Id);

        builder.Property(excursion => excursion.Id)
            .ValueGeneratedNever();

        builder.Property(excursion => excursion.ContainerId)
            .IsRequired();

        builder.Property(excursion => excursion.ReadingId)
            .IsRequired();

        builder.Property(excursion => excursion.Direction)
            .HasConversion<string>()
            .HasMaxLength(ReeferExcursion.DirectionMaxLength)
            .IsRequired();

        builder.Property(excursion => excursion.TemperatureCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(excursion => excursion.ThresholdMinimumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(excursion => excursion.ThresholdMaximumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(excursion => excursion.MeasuredAtUtc)
            .IsRequired();

        builder.Property(excursion => excursion.DetectedAtUtc)
            .IsRequired();

        builder.Property(excursion => excursion.Status)
            .HasConversion<string>()
            .HasMaxLength(ReeferExcursion.StatusMaxLength)
            .IsRequired();

        builder.Property(excursion => excursion.Disposition)
            .HasConversion<string>()
            .HasMaxLength(ReeferExcursion.DispositionMaxLength);

        builder.Property(excursion => excursion.Explanation)
            .HasMaxLength(ReeferExcursion.ExplanationMaxLength);

        builder.Property(excursion => excursion.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(excursion => excursion.ReadingId)
            .IsUnique();

        builder.HasIndex(excursion => excursion.ContainerId)
            .HasDatabaseName(OpenExcursionIndexName)
            .HasFilter(OpenExcursionFilter);

        builder.HasIndex(excursion => new { excursion.ContainerId, excursion.DetectedAtUtc, excursion.Id });

        builder.HasOne<Container>()
            .WithMany()
            .HasForeignKey(excursion => excursion.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReeferTemperatureReading>()
            .WithMany()
            .HasForeignKey(excursion => excursion.ReadingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReeferExcursion>()
            .WithMany()
            .HasForeignKey(excursion => excursion.SupersededByExcursionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
