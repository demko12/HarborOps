using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class PurchaseOrderReceiptConfiguration : IEntityTypeConfiguration<PurchaseOrderReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceipt> builder)
    {
        builder.ToTable("PurchaseOrderReceipts", MaintenanceModule.Schema);

        builder.HasKey(receipt => receipt.Id);

        builder.Property(receipt => receipt.Id)
            .ValueGeneratedNever();

        builder.Property(receipt => receipt.PurchaseOrderId)
            .IsRequired();

        builder.Property(receipt => receipt.ClientRequestId)
            .IsRequired();

        builder.Property(receipt => receipt.RequestHash)
            .HasMaxLength(PurchaseOrderReceipt.RequestHashLength)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.Property(receipt => receipt.ReceivedUtc)
            .IsRequired();

        builder.Property(receipt => receipt.DeliveryReference)
            .HasMaxLength(PurchaseOrderReceipt.DeliveryReferenceMaxLength);

        builder.Property(receipt => receipt.Notes)
            .HasMaxLength(PurchaseOrderReceipt.NotesMaxLength);

        builder.Property(receipt => receipt.RecordedByUserId)
            .IsRequired();

        builder.Property(receipt => receipt.RecordedUtc)
            .IsRequired();

        builder.Property(receipt => receipt.OrderStatusBefore)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(receipt => receipt.OrderStatusAfter)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // A client request id posts at most one receipt, even when competing retries race.
        builder.HasIndex(receipt => receipt.ClientRequestId)
            .IsUnique();

        builder.HasIndex(receipt => new
        {
            receipt.PurchaseOrderId,
            receipt.ReceivedUtc,
            receipt.RecordedUtc,
        });

        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(receipt => receipt.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
