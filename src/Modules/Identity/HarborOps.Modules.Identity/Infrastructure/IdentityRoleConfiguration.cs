using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Identity.Infrastructure;

internal sealed class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("AspNetRoles", IdentityModule.Schema);

        builder.HasKey(role => role.Id);

        builder.HasIndex(role => role.NormalizedName)
            .HasDatabaseName("RoleNameIndex")
            .IsUnique();

        builder.Property(role => role.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Property(role => role.Name)
            .HasMaxLength(256);

        builder.Property(role => role.NormalizedName)
            .HasMaxLength(256);

        builder.HasMany<IdentityRoleClaim<Guid>>()
            .WithOne()
            .HasForeignKey(claim => claim.RoleId)
            .IsRequired();

        builder.HasMany<IdentityUserRole<Guid>>()
            .WithOne()
            .HasForeignKey(userRole => userRole.RoleId)
            .IsRequired();
    }
}
