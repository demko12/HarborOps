using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewCertificateConfiguration : IEntityTypeConfiguration<CrewCertificate>
{
    public void Configure(EntityTypeBuilder<CrewCertificate> builder)
    {
        builder.ToTable("CrewCertificates", CrewModule.Schema);

        builder.HasKey(crewCertificate => crewCertificate.Id);

        builder.Property(crewCertificate => crewCertificate.Id)
            .ValueGeneratedNever();

        builder.Property(crewCertificate => crewCertificate.CrewMemberId)
            .IsRequired();

        builder.Property(crewCertificate => crewCertificate.Type)
            .HasMaxLength(CrewCertificate.TypeMaxLength)
            .IsRequired();

        builder.Property(crewCertificate => crewCertificate.Name)
            .HasMaxLength(CrewCertificate.NameMaxLength)
            .IsRequired();

        builder.Property(crewCertificate => crewCertificate.IssuingAuthority)
            .HasMaxLength(CrewCertificate.IssuingAuthorityMaxLength)
            .IsRequired();

        builder.Property(crewCertificate => crewCertificate.LicenseNumber)
            .HasMaxLength(CrewCertificate.LicenseNumberMaxLength)
            .IsRequired();

        builder.Property(crewCertificate => crewCertificate.IssueDate)
            .IsRequired();

        builder.Property(crewCertificate => crewCertificate.ExpiryDate)
            .IsRequired();

        builder.Property(crewCertificate => crewCertificate.Status)
            .HasMaxLength(CrewCertificate.StatusMaxLength)
            .IsRequired();
    }
}
