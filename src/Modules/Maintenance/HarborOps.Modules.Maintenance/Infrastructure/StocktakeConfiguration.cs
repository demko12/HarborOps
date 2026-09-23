using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class StocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> builder)
    {
        builder.ToTable("Stocktakes", MaintenanceModule.Schema);

        builder.HasKey(stocktake => stocktake.Id);

        builder.Property(stocktake => stocktake.Id)
            .ValueGeneratedNever();

        builder.Property(stocktake => stocktake.Notes)
            .HasMaxLength(Stocktake.NotesMaxLength);

        builder.Property(stocktake => stocktake.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(stocktake => stocktake.CreatedByUserId)
            .IsRequired();

        builder.Property(stocktake => stocktake.CreatedUtc)
            .IsRequired();

        builder.Property(stocktake => stocktake.LastModifiedByUserId);

        builder.Property(stocktake => stocktake.LastModifiedUtc);

        builder.Property(stocktake => stocktake.PostedByUserId);

        builder.Property(stocktake => stocktake.PostedUtc);

        builder.Property(stocktake => stocktake.CancelledByUserId);

        builder.Property(stocktake => stocktake.CancelledUtc);

        builder.Property(stocktake => stocktake.CancellationReason)
            .HasMaxLength(Stocktake.ReasonMaxLength);

        builder.Property(stocktake => stocktake.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(stocktake => stocktake.Status);

        builder.HasIndex(stocktake => new
        {
            stocktake.CreatedUtc,
            stocktake.Id,
        });
    }
}
