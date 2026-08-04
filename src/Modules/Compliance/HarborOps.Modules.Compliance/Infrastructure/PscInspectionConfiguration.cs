using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class PscInspectionConfiguration : IEntityTypeConfiguration<PscInspection>
{
    public void Configure(EntityTypeBuilder<PscInspection> builder)
    {
        builder.ToTable("PscInspections", schema: ComplianceModule.Schema);

        builder.HasKey(p => p.Id);

        builder.Property(p => p.VesselId)
            .IsRequired();

        builder.Property(p => p.PortId)
            .IsRequired();

        builder.Property(p => p.Date)
            .IsRequired();

        builder.Property(p => p.InspectingAuthority)
            .HasMaxLength(PscInspection.InspectingAuthorityMaxLength)
            .IsRequired();

        builder.Property(p => p.DeficiencyCount)
            .IsRequired();

        builder.Property(p => p.VesselDetained)
            .IsRequired();

        builder.HasIndex(p => p.VesselId);

        builder.HasIndex(p => p.PortId);
    }
}
