using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class VoyageEstimateRevisionItemConfiguration
    : IEntityTypeConfiguration<VoyageEstimateRevisionItem>
{
    public void Configure(EntityTypeBuilder<VoyageEstimateRevisionItem> builder)
    {
        builder.ToTable("VoyageEstimateRevisionItems", VoyagesModule.Schema);

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .ValueGeneratedNever();

        builder.Property(item => item.RevisionBatchId)
            .IsRequired();

        builder.Property(item => item.VoyageStopId)
            .IsRequired();

        builder.Property(item => item.PreviousEta)
            .IsRequired();

        builder.Property(item => item.PreviousEtd)
            .IsRequired();

        builder.Property(item => item.NewEta)
            .IsRequired();

        builder.Property(item => item.NewEtd)
            .IsRequired();

        builder.HasOne<VoyageEstimateRevisionBatch>()
            .WithMany()
            .HasForeignKey(item => item.RevisionBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(item => item.VoyageStopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.RevisionBatchId, item.VoyageStopId })
            .IsUnique();
    }
}
