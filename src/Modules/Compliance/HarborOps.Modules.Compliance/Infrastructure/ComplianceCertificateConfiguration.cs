using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class ComplianceCertificateConfiguration : IEntityTypeConfiguration<ComplianceCertificate>
{
    public void Configure(EntityTypeBuilder<ComplianceCertificate> builder)
    {
        builder.ToTable("ComplianceCertificates", schema: ComplianceModule.Schema);

        builder.HasKey(certificate => certificate.Id);

        builder.Property(certificate => certificate.Id)
            .ValueGeneratedNever();

        builder.Property(certificate => certificate.VesselId)
            .IsRequired();

        builder.Property(certificate => certificate.CertificateType)
            .HasMaxLength(ComplianceCertificate.CertificateTypeMaxLength)
            .IsRequired();

        builder.Property(certificate => certificate.CertificateNumber)
            .HasMaxLength(ComplianceCertificate.CertificateNumberMaxLength)
            .IsRequired();

        builder.Property(certificate => certificate.IssuingAuthority)
            .HasMaxLength(ComplianceCertificate.IssuingAuthorityMaxLength)
            .IsRequired();

        builder.Property(certificate => certificate.IssuedOn)
            .IsRequired();

        builder.Property(certificate => certificate.ExpiresOn)
            .IsRequired();

        builder.Property(certificate => certificate.CreatedUtc)
            .IsRequired();

        builder.HasIndex(certificate => certificate.VesselId);

        builder.HasIndex(certificate => certificate.ExpiresOn);
    }
}
