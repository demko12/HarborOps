using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class DelayConfiguration : IEntityTypeConfiguration<Delay>
{
    public void Configure(EntityTypeBuilder<Delay> builder)
    {
        builder.ToTable("Delays", VoyagesModule.Schema);

        builder.HasKey(delay => delay.Id);

        builder.Property(delay => delay.Id)
            .ValueGeneratedNever();

        builder.Property(delay => delay.VoyageId)
            .IsRequired();

        builder.Property(delay => delay.Reason)
            .HasMaxLength(Delay.ReasonMaxLength)
            .IsRequired();

        builder.Property(delay => delay.Duration)
            .IsRequired();

        builder.Property(delay => delay.CreatedAt)
            .IsRequired();

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(delay => delay.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(delay => delay.VoyageStopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
