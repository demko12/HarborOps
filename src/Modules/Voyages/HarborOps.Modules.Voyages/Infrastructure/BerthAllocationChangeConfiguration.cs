using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class BerthAllocationChangeConfiguration : IEntityTypeConfiguration<BerthAllocationChange>
{
    public void Configure(EntityTypeBuilder<BerthAllocationChange> builder)
    {
        builder.ToTable("BerthAllocationChanges", VoyagesModule.Schema);
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.VoyageId).IsRequired();
        builder.Property(change => change.VoyageStopId).IsRequired();
        builder.Property(change => change.NewBerthId).IsRequired();
        builder.Property(change => change.Reason).HasMaxLength(BerthAllocationChange.ReasonMaxLength).IsRequired();
        builder.Property(change => change.ChangedByUserId).IsRequired();
        builder.Property(change => change.ChangedAtUtc).IsRequired();
        builder.Property(change => change.PreviousPlannedArrivalUtc).IsRequired();
        builder.Property(change => change.PreviousPlannedDepartureUtc).IsRequired();
        builder.Property(change => change.NewPlannedArrivalUtc).IsRequired();
        builder.Property(change => change.NewPlannedDepartureUtc).IsRequired();
        builder.HasOne<Voyage>().WithMany().HasForeignKey(change => change.VoyageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<VoyageStop>().WithMany().HasForeignKey(change => change.VoyageStopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Berth>().WithMany().HasForeignKey(change => change.PreviousBerthId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Berth>().WithMany().HasForeignKey(change => change.NewBerthId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.VoyageStopId, change.ChangedAtUtc });
    }
}
