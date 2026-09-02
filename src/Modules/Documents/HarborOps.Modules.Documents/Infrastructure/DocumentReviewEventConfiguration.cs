using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class DocumentReviewEventConfiguration : IEntityTypeConfiguration<DocumentReviewEvent>
{
    public void Configure(EntityTypeBuilder<DocumentReviewEvent> builder)
    {
        builder.ToTable("DocumentReviewEvents", schema: DocumentsModule.Schema);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.DocumentId)
            .IsRequired();

        builder.Property(e => e.Action)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ActorUserId)
            .IsRequired();

        builder.Property(e => e.OccurredAtUtc)
            .IsRequired();

        builder.Property(e => e.Notes)
            .HasMaxLength(DocumentReviewEvent.NotesMaxLength);

        builder.HasOne(e => e.Document)
            .WithMany()
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.DocumentId, e.OccurredAtUtc });
    }
}
