using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class CorrectiveActionConfiguration : IEntityTypeConfiguration<CorrectiveAction>
{
    public void Configure(EntityTypeBuilder<CorrectiveAction> builder)
    {
        builder.ToTable("CorrectiveActions", schema: ComplianceModule.Schema);

        builder.HasKey(action => action.Id);

        builder.Property(action => action.Id)
            .ValueGeneratedNever();

        builder.Property(action => action.NonConformityId)
            .IsRequired();

        builder.Property(action => action.ActionText)
            .HasMaxLength(CorrectiveAction.ActionTextMaxLength)
            .IsRequired();

        builder.Property(action => action.ActorUserId)
            .IsRequired();

        builder.Property(action => action.RecordedAtUtc)
            .IsRequired();

        builder.Property(action => action.EvidenceDocumentId);

        builder.HasOne<NonConformity>()
            .WithMany(nonConformity => nonConformity.CorrectiveActions)
            .HasForeignKey(action => action.NonConformityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
