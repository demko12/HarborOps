using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("RetentionPolicies", schema: DocumentsModule.Schema);

        builder.HasKey(policy => policy.DocumentTypeId);

        builder.Property(policy => policy.DocumentTypeId)
            .ValueGeneratedNever();

        builder.Property(policy => policy.RetentionPeriodDays)
            .IsRequired();

        builder.Property(policy => policy.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<DocumentType>()
            .WithMany()
            .HasForeignKey(policy => policy.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
