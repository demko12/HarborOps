using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class PortCallStatementConfiguration : IEntityTypeConfiguration<PortCallStatement>
{
    public void Configure(EntityTypeBuilder<PortCallStatement> builder)
    {
        builder.ToTable("PortCallStatements", VoyagesModule.Schema);

        builder.HasKey(statement => statement.Id);

        builder.Property(statement => statement.Id)
            .ValueGeneratedNever();

        builder.Property(statement => statement.VoyageId)
            .IsRequired();

        builder.Property(statement => statement.VoyageStopId)
            .IsRequired();

        builder.Property(statement => statement.Status)
            .HasMaxLength(PortCallStatement.StatusMaxLength)
            .IsRequired();

        builder.Property(statement => statement.CreatedByUserId)
            .IsRequired();

        builder.Property(statement => statement.CreatedAtUtc)
            .IsRequired();

        builder.Property(statement => statement.FinalizedByName)
            .HasMaxLength(PortCallStatement.FinalizedByNameMaxLength);

        builder.Property(statement => statement.VoyageNumber)
            .HasMaxLength(Voyage.VoyageNumberMaxLength);

        builder.Property(statement => statement.PortName)
            .HasMaxLength(Port.NameMaxLength);

        builder.Property(statement => statement.PortUnLocode)
            .HasMaxLength(Port.UnLocodeLength);

        builder.Property(statement => statement.PortTimeZone)
            .HasMaxLength(PortCallStatement.PortTimeZoneMaxLength);

        builder.Property(statement => statement.BerthName)
            .HasMaxLength(Berth.NameMaxLength);

        builder.Property(statement => statement.SourceVersion)
            .HasMaxLength(PortCallStatement.SourceVersionMaxLength);

        builder.Property(statement => statement.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<Voyage>()
            .WithMany()
            .HasForeignKey(statement => statement.VoyageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(statement => statement.VoyageStopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(statement => statement.VoyageStopId)
            .IsUnique();
    }
}
