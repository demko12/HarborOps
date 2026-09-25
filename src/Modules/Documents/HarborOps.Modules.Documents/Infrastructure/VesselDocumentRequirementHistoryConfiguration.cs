using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class VesselDocumentRequirementHistoryConfiguration
    : IEntityTypeConfiguration<VesselDocumentRequirementHistory>
{
    public void Configure(EntityTypeBuilder<VesselDocumentRequirementHistory> builder)
    {
        builder.ToTable("VesselDocumentRequirementHistories", DocumentsModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.RequirementId)
            .IsRequired();

        builder.Property(entry => entry.Revision)
            .IsRequired();

        builder.Property(entry => entry.EventType)
            .HasMaxLength(VesselDocumentRequirementHistory.EventTypeMaxLength)
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasMaxLength(VesselDocumentRequirementHistory.ReasonMaxLength);

        builder.Property(entry => entry.IsEnabled)
            .IsRequired();

        builder.Property(entry => entry.MinimumRemainingValidityDays)
            .IsRequired();

        builder.Property(entry => entry.Instruction)
            .HasMaxLength(VesselDocumentRequirement.InstructionMaxLength);

        builder.Property(entry => entry.ActorUserId)
            .IsRequired();

        builder.Property(entry => entry.OccurredAtUtc)
            .IsRequired();

        builder.HasOne<VesselDocumentRequirement>()
            .WithMany()
            .HasForeignKey(entry => entry.RequirementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new { entry.RequirementId, entry.Revision })
            .IsUnique();
    }
}
