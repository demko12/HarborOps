using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure", MaintenanceModule.Schema);

        builder.HasKey(unitOfMeasure => unitOfMeasure.Id);

        builder.Property(unitOfMeasure => unitOfMeasure.Id)
            .ValueGeneratedNever();

        builder.Property(unitOfMeasure => unitOfMeasure.UnitName)
            .HasMaxLength(UnitOfMeasure.UnitNameMaxLength)
            .IsRequired();

        builder.Property(unitOfMeasure => unitOfMeasure.Abbreviation)
            .HasMaxLength(UnitOfMeasure.AbbreviationMaxLength)
            .IsRequired();
    }
}
