using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewEmploymentStatusHistoryConfiguration : IEntityTypeConfiguration<CrewEmploymentStatusHistory>
{
    public void Configure(EntityTypeBuilder<CrewEmploymentStatusHistory> builder)
    {
        builder.ToTable("CrewEmploymentStatusHistories", CrewModule.Schema);

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .ValueGeneratedNever();

        builder.Property(history => history.CrewMemberId)
            .IsRequired();

        builder.Property(history => history.Status)
            .HasMaxLength(CrewEmploymentStatusHistory.StatusMaxLength)
            .IsRequired();

        builder.Property(history => history.ChangedByUserId)
            .IsRequired();

        builder.Property(history => history.Notes)
            .HasMaxLength(CrewEmploymentStatusHistory.NotesMaxLength);

        builder.Property(history => history.ChangedAtUtc)
            .IsRequired();

        builder.HasIndex(history => history.CrewMemberId);
    }
}
