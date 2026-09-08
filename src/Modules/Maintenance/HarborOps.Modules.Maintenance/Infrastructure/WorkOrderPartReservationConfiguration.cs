using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class WorkOrderPartReservationConfiguration : IEntityTypeConfiguration<WorkOrderPartReservation>
{
    public void Configure(EntityTypeBuilder<WorkOrderPartReservation> builder)
    {
        builder.ToTable("WorkOrderPartReservations", MaintenanceModule.Schema);

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Id)
            .ValueGeneratedNever();

        builder.Property(reservation => reservation.WorkOrderId)
            .IsRequired();

        builder.Property(reservation => reservation.InventoryItemId)
            .IsRequired();

        builder.Property(reservation => reservation.QuantityRequested)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(reservation => reservation.QuantityReserved)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(reservation => reservation.QuantityConsumed)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(reservation => reservation.CreatedUtc)
            .IsRequired();

        builder.Property(reservation => reservation.UpdatedUtc)
            .IsRequired();

        builder.HasIndex(reservation => new
        {
            reservation.WorkOrderId,
            reservation.InventoryItemId,
        })
            .IsUnique();

        builder.HasIndex(reservation => reservation.InventoryItemId);

        builder.HasOne<MaintenanceWorkOrder>()
            .WithMany()
            .HasForeignKey(reservation => reservation.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(reservation => reservation.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
