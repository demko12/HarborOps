using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Fleet.Infrastructure;

internal sealed class VesselPositionReportConfiguration : IEntityTypeConfiguration<VesselPositionReport>
{
    public void Configure(EntityTypeBuilder<VesselPositionReport> builder)
    {
        builder.ToTable("VesselPositionReports", FleetModule.Schema);
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Latitude).HasPrecision(9, 6); builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.HasIndex(x => new { x.VesselId, x.ReportedAtUtc, x.Sequence });
        builder.HasIndex(x => new { x.VesselId, x.Sequence }).IsUnique();
    }
}
