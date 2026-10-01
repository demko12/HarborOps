using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class PurchaseOrderClosureConfiguration : IEntityTypeConfiguration<PurchaseOrderClosure>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderClosure> builder)
    {
        builder.ToTable("PurchaseOrderClosures", MaintenanceModule.Schema);

        builder.HasKey(closure => closure.Id);

        builder.Property(closure => closure.Id)
            .ValueGeneratedNever();

        builder.Property(closure => closure.PurchaseOrderId)
            .IsRequired();

        builder.Property(closure => closure.ClientRequestId)
            .IsRequired();

        builder.Property(closure => closure.RequestHash)
            .HasMaxLength(PurchaseOrderClosure.RequestHashLength)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.Property(closure => closure.Reason)
            .HasMaxLength(PurchaseOrderClosure.ReasonMaxLength)
            .IsRequired();

        builder.Property(closure => closure.ClosedByUserId)
            .IsRequired();

        builder.Property(closure => closure.ClosedUtc)
            .IsRequired();

        builder.Property(closure => closure.OrderStatusBefore)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(closure => closure.OrderStatusAfter)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // A client request id closes at most one order, even when competing retries race.
        builder.HasIndex(closure => closure.ClientRequestId)
            .IsUnique();

        // An order is closed at most once, even when competing closes with different client request ids race.
        builder.HasIndex(closure => closure.PurchaseOrderId)
            .IsUnique();

        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(closure => closure.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
