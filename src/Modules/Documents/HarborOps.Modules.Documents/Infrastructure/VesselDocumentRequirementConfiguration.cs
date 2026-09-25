using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class VesselDocumentRequirementConfiguration : IEntityTypeConfiguration<VesselDocumentRequirement>
{
    public void Configure(EntityTypeBuilder<VesselDocumentRequirement> builder)
    {
        builder.ToTable("VesselDocumentRequirements", DocumentsModule.Schema);

        builder.HasKey(requirement => requirement.Id);

        builder.Property(requirement => requirement.Id)
            .ValueGeneratedNever();

        builder.Property(requirement => requirement.VesselId)
            .IsRequired();

        builder.Property(requirement => requirement.DocumentTypeId)
            .IsRequired();

        builder.Property(requirement => requirement.IsEnabled)
            .IsRequired();

        builder.Property(requirement => requirement.MinimumRemainingValidityDays)
            .IsRequired();

        builder.Property(requirement => requirement.Instruction)
            .HasMaxLength(VesselDocumentRequirement.InstructionMaxLength);

        builder.Property(requirement => requirement.Revision)
            .IsRequired();

        builder.Property(requirement => requirement.CreatedByUserId)
            .IsRequired();

        builder.Property(requirement => requirement.CreatedAtUtc)
            .IsRequired();

        builder.Property(requirement => requirement.UpdatedByUserId)
            .IsRequired();

        builder.Property(requirement => requirement.UpdatedAtUtc)
            .IsRequired();

        builder.Property(requirement => requirement.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<DocumentType>()
            .WithMany()
            .HasForeignKey(requirement => requirement.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(requirement => new { requirement.VesselId, requirement.DocumentTypeId })
            .IsUnique();
    }
}
