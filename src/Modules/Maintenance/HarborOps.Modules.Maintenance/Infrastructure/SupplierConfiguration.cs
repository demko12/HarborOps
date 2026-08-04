using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Maintenance.Infrastructure;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", MaintenanceModule.Schema);

        builder.HasKey(supplier => supplier.Id);

        builder.Property(supplier => supplier.Id)
            .ValueGeneratedNever();

        builder.Property(supplier => supplier.Name)
            .HasMaxLength(Supplier.NameMaxLength)
            .IsRequired();

        builder.Property(supplier => supplier.ContactPerson)
            .HasMaxLength(Supplier.ContactPersonMaxLength);

        builder.Property(supplier => supplier.PhoneEmail)
            .HasMaxLength(Supplier.PhoneEmailMaxLength);

        builder.Property(supplier => supplier.Address)
            .HasMaxLength(Supplier.AddressMaxLength);

        builder.Property(supplier => supplier.PaymentTerms)
            .HasMaxLength(Supplier.PaymentTermsMaxLength);
    }
}
