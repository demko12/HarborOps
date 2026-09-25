using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class DocumentDistributionIssueConfiguration : IEntityTypeConfiguration<DocumentDistributionIssue>
{
    public void Configure(EntityTypeBuilder<DocumentDistributionIssue> builder)
    {
        builder.ToTable("DocumentDistributionIssues", DocumentsModule.Schema);
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DocumentNumberSnapshot).HasMaxLength(Document.NumberMaxLength).IsRequired();
        builder.Property(x => x.DocumentTypeSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Instruction).HasMaxLength(DocumentDistributionIssue.InstructionMaxLength);
        builder.Property(x => x.CancellationReason).HasMaxLength(DocumentDistributionIssue.ReasonMaxLength);
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken().HasDefaultValueSql("NEWID()");
        builder.HasIndex(x => x.DocumentId);
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DocumentDistributionRecipientConfiguration : IEntityTypeConfiguration<DocumentDistributionRecipient>
{
    public void Configure(EntityTypeBuilder<DocumentDistributionRecipient> builder)
    {
        builder.ToTable("DocumentDistributionRecipients", DocumentsModule.Schema);
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.State).HasMaxLength(30).IsRequired();
        builder.Property(x => x.StateReason).HasMaxLength(DocumentDistributionIssue.ReasonMaxLength);
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken().HasDefaultValueSql("NEWID()");
        builder.HasIndex(x => new { x.IssueId, x.RecipientUserId }).IsUnique();
        builder.HasIndex(x => new { x.RecipientUserId, x.State });
        builder.HasOne<DocumentDistributionIssue>().WithMany().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Restrict);
    }
}
