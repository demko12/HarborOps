using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortCallClearanceItemConfiguration
    : IEntityTypeConfiguration<PortCallClearanceItem>
{
    public void Configure(EntityTypeBuilder<PortCallClearanceItem> builder)
    {
        builder.ToTable("PortCallClearanceItems", VoyagesModule.Schema);

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .ValueGeneratedNever();

        builder.Property(item => item.PortCallClearanceId)
            .IsRequired();

        builder.Property(item => item.Category)
            .HasMaxLength(PortCallClearanceCategory.MaxLength)
            .IsRequired();

        builder.Property(item => item.Title)
            .HasMaxLength(PortCallClearanceItem.TitleMaxLength)
            .IsRequired();

        builder.Property(item => item.IsRequired)
            .IsRequired();

        builder.Property(item => item.AuthorityReference)
            .HasMaxLength(PortCallClearanceItem.AuthorityReferenceMaxLength);

        builder.Property(item => item.Status)
            .HasMaxLength(PortCallClearanceItem.StatusMaxLength)
            .IsRequired();

        builder.Property(item => item.DecisionReason)
            .HasMaxLength(PortCallClearanceItem.DecisionReasonMaxLength);

        builder.Property(item => item.AddedByUserId)
            .IsRequired();

        builder.Property(item => item.AddedAtUtc)
            .IsRequired();

        builder.HasOne<PortCallClearance>()
            .WithMany()
            .HasForeignKey(item => item.PortCallClearanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(item => new
        {
            item.PortCallClearanceId,
            item.IsRequired,
            item.DueAtUtc,
            item.Category,
            item.Id,
        });
    }
}
