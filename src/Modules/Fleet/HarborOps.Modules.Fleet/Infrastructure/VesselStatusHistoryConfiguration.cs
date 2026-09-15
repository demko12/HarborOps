using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Fleet.Infrastructure;

internal sealed class VesselStatusHistoryConfiguration : IEntityTypeConfiguration<VesselStatusHistory>
{
    public void Configure(EntityTypeBuilder<VesselStatusHistory> builder)
    {
        builder.ToTable("VesselStatusHistories", FleetModule.Schema);

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .ValueGeneratedNever();

        builder.Property(history => history.VesselId)
            .IsRequired();

        builder.Property(history => history.Status)
            .HasMaxLength(VesselStatusHistory.StatusMaxLength)
            .IsRequired();

        builder.Property(history => history.ChangedBy)
            .HasMaxLength(VesselStatusHistory.ChangedByMaxLength)
            .IsRequired();

        builder.Property(history => history.Notes)
            .HasMaxLength(VesselStatusHistory.NotesMaxLength);

        builder.Property(history => history.ChangedAtUtc)
            .IsRequired();

        builder.HasIndex(history => new
        {
            history.VesselId,
            history.ChangedAtUtc,
            history.Id,
        })
            .IsDescending(false, true, true);
    }
}
