using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Compliance.Infrastructure;

internal sealed class SurveyAuditConfiguration : IEntityTypeConfiguration<SurveyAudit>
{
    public void Configure(EntityTypeBuilder<SurveyAudit> builder)
    {
        builder.ToTable("SurveyAudits", schema: ComplianceModule.Schema);

        builder.HasKey(s => s.Id);

        builder.Property(s => s.VesselId)
            .IsRequired();

        builder.Property(s => s.DocumentId);

        builder.Property(s => s.Date)
            .IsRequired();

        builder.Property(s => s.Type)
            .HasMaxLength(SurveyAudit.TypeMaxLength)
            .IsRequired();

        builder.Property(s => s.InspectorName)
            .HasMaxLength(SurveyAudit.InspectorNameMaxLength);

        builder.Property(s => s.Result)
            .HasMaxLength(SurveyAudit.ResultMaxLength)
            .IsRequired();

        builder.Property(s => s.Findings)
            .HasMaxLength(SurveyAudit.FindingsMaxLength)
            .IsRequired();

        builder.HasIndex(s => s.VesselId);

        builder.HasIndex(s => s.DocumentId);
    }
}
