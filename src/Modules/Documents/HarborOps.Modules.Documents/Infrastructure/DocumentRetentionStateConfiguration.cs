using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class DocumentRetentionStateConfiguration : IEntityTypeConfiguration<DocumentRetentionState>
{
    public void Configure(EntityTypeBuilder<DocumentRetentionState> builder)
    {
        builder.ToTable("DocumentRetentionStates", schema: DocumentsModule.Schema, t => t.IsTemporal(h =>
        {
            h.HasPeriodStart("PeriodStart");
            h.HasPeriodEnd("PeriodEnd");
            h.UseHistoryTable("DocumentRetentionStatesHistory", schema: DocumentsModule.Schema);
        }));

        builder.HasKey(state => state.DocumentId);

        builder.Property(state => state.DocumentId)
            .ValueGeneratedNever();

        builder.Property(state => state.State)
            .HasMaxLength(DocumentRetentionState.StateMaxLength)
            .IsRequired();

        builder.Property(state => state.PreDispositionState)
            .HasMaxLength(DocumentRetentionState.StateMaxLength);

        builder.Property(state => state.ArchivedAtUtc);

        builder.Property(state => state.ArchivedByUserId);

        builder.Property(state => state.RestoredAtUtc);

        builder.Property(state => state.RestoredByUserId);

        builder.Property(state => state.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(state => state.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
