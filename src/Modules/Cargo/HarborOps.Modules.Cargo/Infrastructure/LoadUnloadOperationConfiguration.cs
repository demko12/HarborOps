using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class LoadUnloadOperationConfiguration
    : IEntityTypeConfiguration<LoadUnloadOperation>
{
    public void Configure(EntityTypeBuilder<LoadUnloadOperation> builder)
    {
        builder.ToTable("LoadUnloadOperations", CargoModule.Schema, table => table.IsTemporal());

        builder.HasKey(operation => operation.Id);

        builder.Property(operation => operation.Id)
            .ValueGeneratedNever();

        builder.Property(operation => operation.CargoBookingId)
            .IsRequired();

        builder.Property(operation => operation.ResponsibleUserId)
            .IsRequired();

        builder.Property(operation => operation.OperationType)
            .HasConversion<string>()
            .HasMaxLength(LoadUnloadOperation.OperationTypeMaxLength)
            .IsRequired();

        builder.Property(operation => operation.Status)
            .HasConversion<string>()
            .HasMaxLength(LoadUnloadOperation.StatusMaxLength)
            .IsRequired();

        builder.Property(operation => operation.PlannedAtUtc)
            .IsRequired();

        builder.Property(operation => operation.StartedAtUtc);

        builder.Property(operation => operation.FinishedAtUtc);
    }
}
