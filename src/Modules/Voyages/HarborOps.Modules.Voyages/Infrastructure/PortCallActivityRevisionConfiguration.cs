using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortCallActivityRevisionConfiguration
    : IEntityTypeConfiguration<PortCallActivityRevision>
{
    public void Configure(EntityTypeBuilder<PortCallActivityRevision> builder)
    {
        builder.ToTable("PortCallActivityRevisions", VoyagesModule.Schema);

        builder.HasKey(revision => revision.Id);

        builder.Property(revision => revision.Id)
            .ValueGeneratedNever();

        builder.Property(revision => revision.PortCallActivityId)
            .IsRequired();

        builder.Property(revision => revision.Revision)
            .IsRequired();

        builder.Property(revision => revision.ChangeType)
            .HasMaxLength(PortCallActivityRevision.ChangeTypeMaxLength)
            .IsRequired();

        builder.Property(revision => revision.ActivityType)
            .HasMaxLength(PortCallActivityType.MaxLength)
            .IsRequired();

        builder.Property(revision => revision.OccurredAtUtc)
            .IsRequired();

        builder.Property(revision => revision.Notes)
            .HasMaxLength(PortCallActivity.NotesMaxLength);

        builder.Property(revision => revision.Reason)
            .HasMaxLength(PortCallActivityRevision.ReasonMaxLength);

        builder.Property(revision => revision.ActorUserId)
            .IsRequired();

        builder.Property(revision => revision.ActorName)
            .HasMaxLength(PortCallActivityRevision.ActorNameMaxLength)
            .IsRequired();

        builder.Property(revision => revision.ChangedAtUtc)
            .IsRequired();

        builder.HasOne<PortCallActivity>()
            .WithMany()
            .HasForeignKey(revision => revision.PortCallActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(revision => new { revision.PortCallActivityId, revision.Revision })
            .IsUnique();
    }
}
