using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ToTable("DocumentTypes", schema: DocumentsModule.Schema);

        builder.HasKey(dt => dt.Id);

        builder.Property(dt => dt.Name)
            .HasMaxLength(DocumentType.NameMaxLength)
            .IsRequired();

        builder.Property(dt => dt.IssuingAuthority)
            .HasMaxLength(DocumentType.IssuingAuthorityMaxLength)
            .IsRequired();

        builder.Property(dt => dt.MandatoryValidityPeriod)
            .IsRequired();

        builder.Property(dt => dt.RequiresSurvey)
            .IsRequired();
    }
}
