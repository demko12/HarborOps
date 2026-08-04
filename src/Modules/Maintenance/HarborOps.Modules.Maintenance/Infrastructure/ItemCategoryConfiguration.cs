using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> builder)
    {
        builder.ToTable("ItemCategories", MaintenanceModule.Schema);

        builder.HasKey(itemCategory => itemCategory.Id);

        builder.Property(itemCategory => itemCategory.Id)
            .ValueGeneratedNever();

        builder.Property(itemCategory => itemCategory.CategoryName)
            .HasMaxLength(ItemCategory.CategoryNameMaxLength)
            .IsRequired();

        builder.Property(itemCategory => itemCategory.Description)
            .HasMaxLength(ItemCategory.DescriptionMaxLength);
    }
}
