using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortCallStatementEntryConfiguration
    : IEntityTypeConfiguration<PortCallStatementEntry>
{
    public void Configure(EntityTypeBuilder<PortCallStatementEntry> builder)
    {
        builder.ToTable("PortCallStatementEntries", VoyagesModule.Schema);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .ValueGeneratedNever();

        builder.Property(entry => entry.PortCallStatementId)
            .IsRequired();

        builder.Property(entry => entry.Position)
            .IsRequired();

        builder.Property(entry => entry.Source)
            .HasMaxLength(PortCallLogSource.MaxLength)
            .IsRequired();

        builder.Property(entry => entry.Label)
            .HasMaxLength(PortCallStatementEntry.LabelMaxLength)
            .IsRequired();

        builder.Property(entry => entry.OccurredAtUtc)
            .IsRequired();

        builder.HasOne<PortCallStatement>()
            .WithMany()
            .HasForeignKey(entry => entry.PortCallStatementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(entry => new { entry.PortCallStatementId, entry.Position })
            .IsUnique();
    }
}
