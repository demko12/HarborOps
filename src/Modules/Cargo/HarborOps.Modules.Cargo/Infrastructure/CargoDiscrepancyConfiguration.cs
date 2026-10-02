using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class CargoDiscrepancyConfiguration : IEntityTypeConfiguration<CargoDiscrepancy>
{
    internal const string SourceExcursionIndexName = "IX_CargoDiscrepancies_SourceExcursionId";

    public void Configure(EntityTypeBuilder<CargoDiscrepancy> builder)
    {
        builder.ToTable("CargoDiscrepancies", CargoModule.Schema);

        builder.HasKey(discrepancy => discrepancy.Id);

        builder.Property(discrepancy => discrepancy.Id)
            .ValueGeneratedNever();

        builder.Property(discrepancy => discrepancy.CargoBookingId)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.Type)
            .HasConversion<string>()
            .HasMaxLength(CargoDiscrepancy.TypeMaxLength)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.Severity)
            .HasConversion<string>()
            .HasMaxLength(CargoDiscrepancy.SeverityMaxLength)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.Description)
            .HasMaxLength(CargoDiscrepancy.DescriptionMaxLength)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.ObservedAtUtc)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.ReportedByUserId)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.ReportedByName)
            .HasMaxLength(CargoDiscrepancy.ActorNameMaxLength);

        builder.Property(discrepancy => discrepancy.ResolvedByName)
            .HasMaxLength(CargoDiscrepancy.ActorNameMaxLength);

        builder.Property(discrepancy => discrepancy.VoidedByName)
            .HasMaxLength(CargoDiscrepancy.ActorNameMaxLength);

        builder.Property(discrepancy => discrepancy.ReportedAtUtc)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.Status)
            .HasConversion<string>()
            .HasMaxLength(CargoDiscrepancy.StatusMaxLength)
            .IsRequired();

        builder.Property(discrepancy => discrepancy.Disposition)
            .HasConversion<string>()
            .HasMaxLength(CargoDiscrepancy.DispositionMaxLength);

        builder.Property(discrepancy => discrepancy.SourceDirection)
            .HasConversion<string>()
            .HasMaxLength(ReeferExcursion.DirectionMaxLength);

        builder.Property(discrepancy => discrepancy.SourceTemperatureCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(discrepancy => discrepancy.SourceThresholdMinimumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(discrepancy => discrepancy.SourceThresholdMaximumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(discrepancy => discrepancy.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(discrepancy => discrepancy.SourceExcursionId)
            .HasDatabaseName(SourceExcursionIndexName)
            .IsUnique()
            .HasFilter("[SourceExcursionId] IS NOT NULL");

        builder.HasIndex(discrepancy => new { discrepancy.CargoBookingId, discrepancy.Status });

        builder.HasIndex(discrepancy => new
        {
            discrepancy.CargoBookingId,
            discrepancy.ReportedAtUtc,
            discrepancy.Id,
        });

        builder.HasIndex(discrepancy => discrepancy.ContainerId);

        builder.HasOne<CargoBooking>()
            .WithMany()
            .HasForeignKey(discrepancy => discrepancy.CargoBookingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Container>()
            .WithMany()
            .HasForeignKey(discrepancy => discrepancy.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReeferExcursion>()
            .WithMany()
            .HasForeignKey(discrepancy => discrepancy.SourceExcursionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
