using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class VoyageLegConfiguration : IEntityTypeConfiguration<VoyageLeg>
{
    public void Configure(EntityTypeBuilder<VoyageLeg> builder)
    {
        builder.ToTable("VoyageLegs", VoyagesModule.Schema);

        builder.HasKey(voyageLeg => voyageLeg.Id);

        builder.Property(voyageLeg => voyageLeg.Id)
            .ValueGeneratedNever();

        builder.Property(voyageLeg => voyageLeg.VoyageId)
            .IsRequired();

        builder.Property(voyageLeg => voyageLeg.FromStopId)
            .IsRequired();

        builder.Property(voyageLeg => voyageLeg.ToStopId)
            .IsRequired();

        builder.Property(voyageLeg => voyageLeg.Distance)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(voyageLeg => voyageLeg.EstimatedDuration)
            .IsRequired();

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(voyageLeg => voyageLeg.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(voyageLeg => voyageLeg.FromStopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(voyageLeg => voyageLeg.ToStopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
