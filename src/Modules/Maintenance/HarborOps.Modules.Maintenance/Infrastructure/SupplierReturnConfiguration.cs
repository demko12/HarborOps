using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> builder)
    {
        builder.ToTable("SupplierReturns", MaintenanceModule.Schema);

        builder.HasKey(supplierReturn => supplierReturn.Id);

        builder.Property(supplierReturn => supplierReturn.Id)
            .ValueGeneratedNever();

        builder.Property(supplierReturn => supplierReturn.PurchaseOrderId)
            .IsRequired();

        builder.Property(supplierReturn => supplierReturn.SupplierId)
            .IsRequired();

        builder.Property(supplierReturn => supplierReturn.Notes)
            .HasMaxLength(SupplierReturn.NotesMaxLength);

        builder.Property(supplierReturn => supplierReturn.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(supplierReturn => supplierReturn.CreatedByUserId)
            .IsRequired();

        builder.Property(supplierReturn => supplierReturn.CreatedUtc)
            .IsRequired();

        builder.Property(supplierReturn => supplierReturn.LastModifiedByUserId);

        builder.Property(supplierReturn => supplierReturn.LastModifiedUtc);

        builder.Property(supplierReturn => supplierReturn.PostedByUserId);

        builder.Property(supplierReturn => supplierReturn.PostedUtc);

        builder.Property(supplierReturn => supplierReturn.CancelledByUserId);

        builder.Property(supplierReturn => supplierReturn.CancelledUtc);

        builder.Property(supplierReturn => supplierReturn.CancellationReason)
            .HasMaxLength(SupplierReturn.ReasonMaxLength);

        builder.Property(supplierReturn => supplierReturn.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(supplierReturn => new
        {
            supplierReturn.PurchaseOrderId,
            supplierReturn.Status,
        });

        builder.HasIndex(supplierReturn => supplierReturn.Status);

        builder.HasIndex(supplierReturn => new
        {
            supplierReturn.CreatedUtc,
            supplierReturn.Id,
        });

        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(supplierReturn => supplierReturn.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(supplierReturn => supplierReturn.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
