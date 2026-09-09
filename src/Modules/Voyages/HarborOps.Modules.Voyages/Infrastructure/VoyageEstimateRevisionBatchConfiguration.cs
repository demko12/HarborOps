using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class VoyageEstimateRevisionBatchConfiguration
    : IEntityTypeConfiguration<VoyageEstimateRevisionBatch>
{
    public void Configure(EntityTypeBuilder<VoyageEstimateRevisionBatch> builder)
    {
        builder.ToTable("VoyageEstimateRevisionBatches", VoyagesModule.Schema);

        builder.HasKey(batch => batch.Id);

        builder.Property(batch => batch.Id)
            .ValueGeneratedNever();

        builder.Property(batch => batch.VoyageId)
            .IsRequired();

        builder.Property(batch => batch.DelayId)
            .IsRequired();

        builder.Property(batch => batch.Reason)
            .HasMaxLength(VoyageEstimateRevisionBatch.ReasonMaxLength)
            .IsRequired();

        builder.Property(batch => batch.RevisedByUserId)
            .IsRequired();

        builder.Property(batch => batch.RevisedAtUtc)
            .IsRequired();

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(batch => batch.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Delay>()
            .WithMany()
            .HasForeignKey(batch => batch.DelayId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(batch => batch.DelayId)
            .IsUnique();

        builder.HasIndex(batch => new { batch.VoyageId, batch.RevisedAtUtc });
    }
}
