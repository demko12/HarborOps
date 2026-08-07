using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Identity;

public sealed class IdentityModelMappingTests
{
    [Theory]
    [InlineData(typeof(IdentityUser<Guid>), "AspNetUsers")]
    [InlineData(typeof(IdentityRole<Guid>), "AspNetRoles")]
    [InlineData(typeof(IdentityUserRole<Guid>), "AspNetUserRoles")]
    [InlineData(typeof(IdentityUserClaim<Guid>), "AspNetUserClaims")]
    [InlineData(typeof(IdentityRoleClaim<Guid>), "AspNetRoleClaims")]
    [InlineData(typeof(IdentityUserLogin<Guid>), "AspNetUserLogins")]
    [InlineData(typeof(IdentityUserToken<Guid>), "AspNetUserTokens")]
    public void Standard_identity_entities_are_mapped_to_the_identity_schema(
        Type entityClrType,
        string expectedTableName)
    {
        using var dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(entityClrType);

        Assert.NotNull(entityType);
        Assert.Equal(expectedTableName, entityType!.GetTableName());
        Assert.Equal(IdentityModule.Schema, entityType.GetSchema());
    }

    [Theory]
    [InlineData(typeof(IdentityUser<Guid>), "Id")]
    [InlineData(typeof(IdentityRole<Guid>), "Id")]
    [InlineData(typeof(IdentityUserRole<Guid>), "UserId")]
    [InlineData(typeof(IdentityUserRole<Guid>), "RoleId")]
    [InlineData(typeof(IdentityUserClaim<Guid>), "UserId")]
    [InlineData(typeof(IdentityRoleClaim<Guid>), "RoleId")]
    [InlineData(typeof(IdentityUserLogin<Guid>), "UserId")]
    [InlineData(typeof(IdentityUserToken<Guid>), "UserId")]
    public void Identity_primary_and_foreign_keys_use_guid(Type entityClrType, string propertyName)
    {
        using var dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(entityClrType);
        var property = entityType!.FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(typeof(Guid), property!.ClrType);
    }

    [Fact]
    public void AspNetUsers_normalized_username_index_is_unique()
    {
        using var dbContext = CreateDbContext();

        var entityType = dbContext.Model.FindEntityType(typeof(IdentityUser<Guid>));
        var index = entityType!
            .GetIndexes()
            .Single(candidate => candidate.Properties.Single().Name == nameof(IdentityUser<Guid>.NormalizedUserName));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Existing_module_schema_mappings_remain_unchanged()
    {
        using var dbContext = CreateDbContext();

        var vesselEntityType = dbContext.Model.FindEntityType(typeof(Vessel));

        Assert.NotNull(vesselEntityType);
        Assert.Equal("Vessels", vesselEntityType!.GetTableName());
        Assert.Equal("fleet", vesselEntityType.GetSchema());
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new HarborOpsDbContext(options);
    }
}
