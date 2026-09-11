using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class DispositionRequestConfiguration : IEntityTypeConfiguration<DispositionRequest>
{
    public void Configure(EntityTypeBuilder<DispositionRequest> builder)
    {
        builder.ToTable("DispositionRequests", schema: DocumentsModule.Schema);

        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id)
            .ValueGeneratedNever();

        builder.Property(request => request.DocumentId)
            .IsRequired();

        builder.Property(request => request.RequestedByUserId)
            .IsRequired();

        builder.Property(request => request.RequestedAtUtc)
            .IsRequired();

        builder.Property(request => request.Reason)
            .HasMaxLength(DispositionRequest.ReasonMaxLength)
            .IsRequired();

        builder.Property(request => request.Status)
            .HasMaxLength(DispositionRequest.StatusMaxLength)
            .IsRequired();

        builder.Property(request => request.DecidedByUserId);

        builder.Property(request => request.DecidedAtUtc);

        builder.Property(request => request.DecisionReason)
            .HasMaxLength(DispositionRequest.DecisionReasonMaxLength);

        builder.Property(request => request.SnapshotRetentionPeriodDays)
            .IsRequired();

        builder.Property(request => request.SnapshotLifecycleTriggerAtUtc)
            .IsRequired();

        builder.Property(request => request.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(request => request.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(request => new { request.DocumentId, request.Status });
    }
}
