using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> builder)
    {
        builder.ToTable(
            "Equipment",
            MaintenanceModule.Schema,
            table => table.IsTemporal());

        builder.HasKey(equipment => equipment.Id);

        builder.Property(equipment => equipment.Id)
            .ValueGeneratedNever();

        builder.Property(equipment => equipment.VesselId)
            .IsRequired();

        builder.Property(equipment => equipment.Name)
            .HasMaxLength(Equipment.NameMaxLength)
            .IsRequired();

        builder.Property(equipment => equipment.Category)
            .HasMaxLength(Equipment.CategoryMaxLength)
            .IsRequired();

        builder.Property(equipment => equipment.Manufacturer)
            .HasMaxLength(Equipment.ManufacturerMaxLength);

        builder.Property(equipment => equipment.SerialNumber)
            .HasMaxLength(Equipment.SerialNumberMaxLength);
    }
}
