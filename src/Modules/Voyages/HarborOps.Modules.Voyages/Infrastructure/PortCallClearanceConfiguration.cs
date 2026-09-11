using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortCallClearanceConfiguration : IEntityTypeConfiguration<PortCallClearance>
{
    public void Configure(EntityTypeBuilder<PortCallClearance> builder)
    {
        builder.ToTable("PortCallClearances", VoyagesModule.Schema);

        builder.HasKey(clearance => clearance.Id);

        builder.Property(clearance => clearance.Id)
            .ValueGeneratedNever();

        builder.Property(clearance => clearance.VoyageId)
            .IsRequired();

        builder.Property(clearance => clearance.VoyageStopId)
            .IsRequired();

        builder.Property(clearance => clearance.Status)
            .HasMaxLength(PortCallClearance.StatusMaxLength)
            .IsRequired();

        builder.Property(clearance => clearance.LastEventSequence)
            .IsRequired();

        builder.Property(clearance => clearance.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(clearance => clearance.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(clearance => clearance.VoyageStopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(clearance => clearance.VoyageStopId)
            .IsUnique();
    }
}
