using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class CargoSafetyReviewEntryConfiguration
    : IEntityTypeConfiguration<CargoSafetyReviewEntry>
{
    public void Configure(EntityTypeBuilder<CargoSafetyReviewEntry> builder)
    {
        builder.ToTable("CargoSafetyReviews", CargoModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.CargoSafetyInfoId)
            .IsRequired();

        builder.Property(entry => entry.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(CargoSafetyReviewEntry.StatusMaxLength)
            .IsRequired();

        builder.Property(entry => entry.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(CargoSafetyReviewEntry.StatusMaxLength)
            .IsRequired();

        builder.Property(entry => entry.ActorUserId)
            .IsRequired();

        builder.Property(entry => entry.OccurredUtc)
            .IsRequired();

        builder.Property(entry => entry.Notes)
            .HasMaxLength(CargoSafetyInfo.ReviewNotesMaxLength);

        builder.HasIndex(entry => entry.CargoSafetyInfoId);

        builder.HasOne<CargoSafetyInfo>()
            .WithMany()
            .HasForeignKey(entry => entry.CargoSafetyInfoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
