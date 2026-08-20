using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents", schema: DocumentsModule.Schema, t => t.IsTemporal(h =>
        {
            h.HasPeriodStart("PeriodStart");
            h.HasPeriodEnd("PeriodEnd");
            h.UseHistoryTable("DocumentsHistory", schema: DocumentsModule.Schema);
        }));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.VesselId)
            .IsRequired();

        builder.Property(d => d.UploadedBy);

        builder.Property(d => d.Number)
            .HasMaxLength(Document.NumberMaxLength)
            .IsRequired();

        builder.Property(d => d.IssueDate)
            .IsRequired();

        builder.Property(d => d.ExpiryDate)
            .IsRequired();

        builder.Property(d => d.IssuingAuthority)
            .HasMaxLength(Document.IssuingAuthorityMaxLength)
            .IsRequired();

        builder.Property(d => d.Status)
            .HasMaxLength(Document.StatusMaxLength)
            .IsRequired();

        builder.Property(d => d.FileName)
            .HasMaxLength(Document.FileNameMaxLength);

        builder.Property(d => d.StorageKey)
            .HasMaxLength(Document.StorageKeyMaxLength);

        builder.Property(d => d.ContentType)
            .HasMaxLength(Document.ContentTypeMaxLength);

        builder.Property(d => d.SizeBytes);

        builder.HasOne(d => d.DocumentType)
            .WithMany()
            .HasForeignKey(d => d.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.VesselId);

        builder.HasIndex(d => d.ExpiryDate);
    }
}
