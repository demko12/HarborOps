using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class LegalHoldConfiguration : IEntityTypeConfiguration<LegalHold>
{
    public void Configure(EntityTypeBuilder<LegalHold> builder)
    {
        builder.ToTable("LegalHolds", schema: DocumentsModule.Schema);

        builder.HasKey(hold => hold.Id);

        builder.Property(hold => hold.Id)
            .ValueGeneratedNever();

        builder.Property(hold => hold.DocumentId)
            .IsRequired();

        builder.Property(hold => hold.Reason)
            .HasMaxLength(LegalHold.ReasonMaxLength)
            .IsRequired();

        builder.Property(hold => hold.Reference)
            .HasMaxLength(LegalHold.ReferenceMaxLength);

        builder.Property(hold => hold.PlacedByUserId)
            .IsRequired();

        builder.Property(hold => hold.PlacedAtUtc)
            .IsRequired();

        builder.Property(hold => hold.IsActive)
            .IsRequired();

        builder.Property(hold => hold.ReleaseReason)
            .HasMaxLength(LegalHold.ReleaseReasonMaxLength);

        builder.Property(hold => hold.ReleasedByUserId);

        builder.Property(hold => hold.ReleasedAtUtc);

        builder.Property(hold => hold.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(hold => hold.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(hold => new { hold.DocumentId, hold.IsActive });
    }
}
