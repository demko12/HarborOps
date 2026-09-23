using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortCallActivityConfiguration : IEntityTypeConfiguration<PortCallActivity>
{
    public void Configure(EntityTypeBuilder<PortCallActivity> builder)
    {
        builder.ToTable("PortCallActivities", VoyagesModule.Schema);

        builder.HasKey(activity => activity.Id);

        builder.Property(activity => activity.Id)
            .ValueGeneratedNever();

        builder.Property(activity => activity.VoyageId)
            .IsRequired();

        builder.Property(activity => activity.VoyageStopId)
            .IsRequired();

        builder.Property(activity => activity.Sequence)
            .IsRequired();

        builder.Property(activity => activity.ActivityType)
            .HasMaxLength(PortCallActivityType.MaxLength)
            .IsRequired();

        builder.Property(activity => activity.OccurredAtUtc)
            .IsRequired();

        builder.Property(activity => activity.Notes)
            .HasMaxLength(PortCallActivity.NotesMaxLength);

        builder.Property(activity => activity.Status)
            .HasMaxLength(PortCallActivity.StatusMaxLength)
            .IsRequired();

        builder.Property(activity => activity.Revision)
            .IsRequired();

        builder.Property(activity => activity.IsLocked)
            .IsRequired();

        builder.Property(activity => activity.RecordedByUserId)
            .IsRequired();

        builder.Property(activity => activity.RecordedAtUtc)
            .IsRequired();

        builder.Property(activity => activity.LastChangedByUserId)
            .IsRequired();

        builder.Property(activity => activity.LastChangedAtUtc)
            .IsRequired();

        builder.Property(activity => activity.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(activity => activity.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(activity => activity.VoyageStopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(activity => new { activity.VoyageStopId, activity.Sequence })
            .IsUnique();
    }
}
