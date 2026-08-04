using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class InsurancePolicyConfiguration : IEntityTypeConfiguration<InsurancePolicy>
{
    public void Configure(EntityTypeBuilder<InsurancePolicy> builder)
    {
        builder.ToTable("InsurancePolicies", schema: ComplianceModule.Schema);

        builder.HasKey(p => p.Id);

        builder.Property(p => p.VesselId)
            .IsRequired();

        builder.Property(p => p.Insurer)
            .HasMaxLength(InsurancePolicy.InsurerMaxLength)
            .IsRequired();

        builder.Property(p => p.PolicyNumber)
            .HasMaxLength(InsurancePolicy.PolicyNumberMaxLength)
            .IsRequired();

        builder.Property(p => p.Coverage)
            .HasMaxLength(InsurancePolicy.CoverageMaxLength)
            .IsRequired();

        builder.Property(p => p.ExpiryDate)
            .IsRequired();

        builder.HasIndex(p => p.VesselId);

        builder.HasIndex(p => p.ExpiryDate);
    }
}
