using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class ContainerControlConfiguration : IEntityTypeConfiguration<ContainerControl>
{
    private const string ActiveSealIndexName = "IX_ContainerControls_ActiveSealNumber";

    private const string ActiveSealFilter =
        "[SealNumber] IS NOT NULL AND [SealStatus] = 'Intact'";

    public void Configure(EntityTypeBuilder<ContainerControl> builder)
    {
        builder.ToTable("ContainerControls", CargoModule.Schema, table => table.IsTemporal());

        builder.HasKey(control => control.Id);

        builder.Property(control => control.Id)
            .ValueGeneratedNever();

        builder.Property(control => control.ContainerId)
            .IsRequired();

        builder.Property(control => control.SealNumber)
            .HasMaxLength(ContainerControl.SealNumberMaxLength)
            .IsUnicode(false);

        builder.Property(control => control.SealStatus)
            .HasConversion<string>()
            .HasMaxLength(ContainerControl.SealStatusMaxLength)
            .IsRequired();

        builder.Property(control => control.State)
            .HasConversion<string>()
            .HasMaxLength(ContainerControl.StateMaxLength)
            .IsRequired();

        builder.Property(control => control.LatestInspectionResult)
            .HasConversion<string>()
            .HasMaxLength(ContainerControl.InspectionResultMaxLength);

        builder.Property(control => control.LatestInspectionNotes)
            .HasMaxLength(ContainerControl.InspectionNotesMaxLength);

        builder.Property(control => control.LatestInspectionTemperatureCelsius)
            .HasPrecision(
                ContainerControl.TemperaturePrecision,
                ContainerControl.TemperatureScale);

        builder.Property(control => control.StateReason)
            .HasMaxLength(ContainerControl.ReasonMaxLength);

        builder.Property(control => control.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(control => control.ContainerId)
            .IsUnique();

        builder.HasIndex(control => control.SealNumber)
            .HasDatabaseName(ActiveSealIndexName)
            .HasFilter(ActiveSealFilter)
            .IsUnique();

        builder.HasOne<Container>()
            .WithMany()
            .HasForeignKey(control => control.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
