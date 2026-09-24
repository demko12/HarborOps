using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable(
            "InventoryItems",
            MaintenanceModule.Schema,
            table => table.IsTemporal());

        builder.HasKey(inventoryItem => inventoryItem.Id);

        builder.Property(inventoryItem => inventoryItem.Id)
            .ValueGeneratedNever();

        builder.Property(inventoryItem => inventoryItem.CategoryId)
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.UnitId)
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.SupplierId)
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.Name)
            .HasMaxLength(InventoryItem.NameMaxLength)
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.PartNumber)
            .HasMaxLength(InventoryItem.PartNumberMaxLength)
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.Quantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.MinimumQuantity)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.UnitCost)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(inventoryItem => inventoryItem.StockConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.Property(inventoryItem => inventoryItem.ReservationConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasOne<ItemCategory>()
            .WithMany()
            .HasForeignKey(inventoryItem => inventoryItem.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(inventoryItem => inventoryItem.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(inventoryItem => inventoryItem.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
