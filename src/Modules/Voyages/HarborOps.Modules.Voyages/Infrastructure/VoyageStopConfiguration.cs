using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class VoyageStopConfiguration : IEntityTypeConfiguration<VoyageStop>
{
    public void Configure(EntityTypeBuilder<VoyageStop> builder)
    {
        builder.ToTable("VoyageStops", VoyagesModule.Schema);

        builder.HasKey(voyageStop => voyageStop.Id);

        builder.Property(voyageStop => voyageStop.Id)
            .ValueGeneratedNever();

        builder.Property(voyageStop => voyageStop.VoyageId)
            .IsRequired();

        builder.Property(voyageStop => voyageStop.PortId)
            .IsRequired();

        builder.Property(voyageStop => voyageStop.StopOrder)
            .IsRequired();

        builder.Property(voyageStop => voyageStop.Eta)
            .IsRequired();

        builder.Property(voyageStop => voyageStop.Etd)
            .IsRequired();

        builder.Property(voyageStop => voyageStop.Status)
            .HasMaxLength(VoyageStop.StatusMaxLength)
            .IsRequired();

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(voyageStop => voyageStop.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(voyageStop => voyageStop.PortId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Berth>()
            .WithMany()
            .HasForeignKey(voyageStop => voyageStop.BerthId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
