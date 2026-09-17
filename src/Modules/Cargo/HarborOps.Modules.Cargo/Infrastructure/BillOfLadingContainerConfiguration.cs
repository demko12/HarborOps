using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class BillOfLadingContainerConfiguration
    : IEntityTypeConfiguration<BillOfLadingContainer>
{
    private const string ActiveSelectionIndexName =
        "IX_BillOfLadingContainers_ActiveContainer";

    private const string ActiveSelectionFilter = "[IsActiveSelection] = 1";

    public void Configure(EntityTypeBuilder<BillOfLadingContainer> builder)
    {
        builder.ToTable("BillOfLadingContainers", CargoModule.Schema);

        builder.HasKey(selection => selection.Id);

        builder.Property(selection => selection.Id)
            .ValueGeneratedNever();

        builder.Property(selection => selection.BillOfLadingId)
            .IsRequired();

        builder.Property(selection => selection.ContainerId)
            .IsRequired();

        builder.Property(selection => selection.IsActiveSelection)
            .IsRequired();

        builder.Property(selection => selection.SnapshotContainerNumber)
            .HasMaxLength(Container.ContainerNumberMaxLength)
            .IsUnicode(false);

        builder.Property(selection => selection.SnapshotContainerType)
            .HasMaxLength(Container.ContainerTypeMaxLength);

        builder.HasIndex(selection => new { selection.BillOfLadingId, selection.ContainerId })
            .IsUnique();

        builder.HasIndex(selection => selection.ContainerId)
            .HasDatabaseName(ActiveSelectionIndexName)
            .HasFilter(ActiveSelectionFilter)
            .IsUnique();

        builder.HasOne<BillOfLading>()
            .WithMany()
            .HasForeignKey(selection => selection.BillOfLadingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Container>()
            .WithMany()
            .HasForeignKey(selection => selection.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
