using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class BerthConfiguration : IEntityTypeConfiguration<Berth>
{
    public void Configure(EntityTypeBuilder<Berth> builder)
    {
        builder.ToTable("Berths", VoyagesModule.Schema);

        builder.HasKey(berth => berth.Id);

        builder.Property(berth => berth.Id)
            .ValueGeneratedNever();

        builder.Property(berth => berth.PortId)
            .IsRequired();

        builder.Property(berth => berth.Name)
            .HasMaxLength(Berth.NameMaxLength)
            .IsRequired();

        builder.Property(berth => berth.Length)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(berth => berth.MaxDraft)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(berth => berth.Status)
            .HasMaxLength(Berth.StatusMaxLength)
            .IsRequired();

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(berth => berth.PortId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
