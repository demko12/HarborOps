using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Identity.Infrastructure;

internal sealed class IdentityUserConfiguration : IEntityTypeConfiguration<IdentityUser<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUser<Guid>> builder)
    {
        builder.ToTable("AspNetUsers", IdentityModule.Schema);

        builder.HasKey(user => user.Id);

        builder.HasIndex(user => user.NormalizedUserName)
            .HasDatabaseName("UserNameIndex")
            .IsUnique();

        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("EmailIndex");

        builder.Property(user => user.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Property(user => user.UserName)
            .HasMaxLength(256);

        builder.Property(user => user.NormalizedUserName)
            .HasMaxLength(256);

        builder.Property(user => user.Email)
            .HasMaxLength(256);

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(256);

        builder.HasMany<IdentityUserClaim<Guid>>()
            .WithOne()
            .HasForeignKey(claim => claim.UserId)
            .IsRequired();

        builder.HasMany<IdentityUserLogin<Guid>>()
            .WithOne()
            .HasForeignKey(login => login.UserId)
            .IsRequired();

        builder.HasMany<IdentityUserToken<Guid>>()
            .WithOne()
            .HasForeignKey(token => token.UserId)
            .IsRequired();

        builder.HasMany<IdentityUserRole<Guid>>()
            .WithOne()
            .HasForeignKey(userRole => userRole.UserId)
            .IsRequired();
    }
}
