using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class ReeferMonitoringProfileConfiguration
    : IEntityTypeConfiguration<ReeferMonitoringProfile>
{
    public void Configure(EntityTypeBuilder<ReeferMonitoringProfile> builder)
    {
        builder.ToTable("ReeferMonitoringProfiles", CargoModule.Schema);

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Id)
            .ValueGeneratedNever();

        builder.Property(profile => profile.ContainerId)
            .IsRequired();

        builder.Property(profile => profile.MinimumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(profile => profile.MaximumCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(profile => profile.TargetCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale)
            .IsRequired();

        builder.Property(profile => profile.Version)
            .IsRequired();

        builder.Property(profile => profile.CreatedByUserId)
            .IsRequired();

        builder.Property(profile => profile.CreatedAtUtc)
            .IsRequired();

        builder.Property(profile => profile.UpdatedByUserId)
            .IsRequired();

        builder.Property(profile => profile.UpdatedAtUtc)
            .IsRequired();

        builder.Property(profile => profile.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(profile => profile.ContainerId)
            .IsUnique();

        builder.HasOne<Container>()
            .WithMany()
            .HasForeignKey(profile => profile.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
