using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class CargoSafetyInfoConfiguration : IEntityTypeConfiguration<CargoSafetyInfo>
{
    public void Configure(EntityTypeBuilder<CargoSafetyInfo> builder)
    {
        builder.ToTable("CargoSafetyInfos", CargoModule.Schema, table => table.IsTemporal());

        builder.HasKey(safetyInfo => safetyInfo.Id);

        builder.Property(safetyInfo => safetyInfo.Id)
            .ValueGeneratedNever();

        builder.Property(safetyInfo => safetyInfo.CargoBookingId)
            .IsRequired();

        builder.Property(safetyInfo => safetyInfo.ImdgClass)
            .HasMaxLength(CargoSafetyInfo.ImdgClassMaxLength)
            .IsRequired();

        builder.Property(safetyInfo => safetyInfo.UnNumber)
            .HasMaxLength(CargoSafetyInfo.UnNumberMaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(safetyInfo => safetyInfo.StorageConditions)
            .HasMaxLength(CargoSafetyInfo.StorageConditionsMaxLength)
            .IsRequired();

        builder.Property(safetyInfo => safetyInfo.HandlingNotes)
            .HasMaxLength(CargoSafetyInfo.HandlingNotesMaxLength);

        builder.Property(safetyInfo => safetyInfo.ReviewStatus)
            .HasConversion<string>()
            .HasMaxLength(CargoSafetyInfo.ReviewStatusMaxLength)
            .IsRequired();

        builder.Property(safetyInfo => safetyInfo.ReviewNotes)
            .HasMaxLength(CargoSafetyInfo.ReviewNotesMaxLength);

        builder.HasIndex(safetyInfo => safetyInfo.CargoBookingId)
            .IsUnique();

        builder.HasOne<CargoBooking>()
            .WithMany()
            .HasForeignKey(safetyInfo => safetyInfo.CargoBookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
