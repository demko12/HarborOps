using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class ReeferProfileChangeConfiguration
    : IEntityTypeConfiguration<ReeferProfileChange>
{
    public void Configure(EntityTypeBuilder<ReeferProfileChange> builder)
    {
        builder.ToTable("ReeferProfileChanges", CargoModule.Schema);

        builder.HasKey(change => change.Id);

        builder.Property(change => change.Id)
            .ValueGeneratedNever();

        builder.Property(change => change.ReeferMonitoringProfileId)
            .IsRequired();

        builder.Property(change => change.Version)
            .IsRequired();

        builder.Property(change => change.FromMinimumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(change => change.FromMaximumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(change => change.FromTargetCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(change => change.ToMinimumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(change => change.ToMaximumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(change => change.ToTargetCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(change => change.Reason)
            .HasMaxLength(ReeferMonitoringProfile.ReasonMaxLength);

        builder.Property(change => change.ActorUserId)
            .IsRequired();

        builder.Property(change => change.OccurredUtc)
            .IsRequired();

        builder.HasIndex(change => new { change.ReeferMonitoringProfileId, change.Version })
            .IsUnique();

        builder.HasOne<ReeferMonitoringProfile>()
            .WithMany()
            .HasForeignKey(change => change.ReeferMonitoringProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
