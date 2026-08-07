using HarborOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Identity;

public sealed class IdentityAccountTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public IdentityAccountTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task UserManager_creates_and_retrieves_an_account_using_the_configured_store()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var email = $"master.mariner.{Guid.NewGuid():N}@harborops.test";
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
        };

        var createResult = await userManager.CreateAsync(user, "P@ssw0rd!2026");

        Assert.True(createResult.Succeeded);
        Assert.NotEqual(Guid.Empty, user.Id);

        var retrieved = await userManager.FindByIdAsync(user.Id.ToString());

        Assert.NotNull(retrieved);
        Assert.Equal(email, retrieved!.Email);

        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext
            .Set<IdentityUser<Guid>>()
            .SingleAsync(candidate => candidate.Id == user.Id);

        Assert.Equal(email.ToUpperInvariant(), persisted.NormalizedEmail);
    }

    [Fact]
    public async Task UserManager_rejects_duplicate_normalized_email()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var email = $"duplicate.{Guid.NewGuid():N}@harborops.test";

        var firstUser = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
        };
        var firstResult = await userManager.CreateAsync(firstUser, "P@ssw0rd!2026");

        Assert.True(firstResult.Succeeded);

        var secondUser = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            UserName = $"different-username-{Guid.NewGuid():N}",
            Email = email.ToUpperInvariant(),
        };
        var secondResult = await userManager.CreateAsync(secondUser, "P@ssw0rd!2026");

        Assert.False(secondResult.Succeeded);
        Assert.Contains(
            secondResult.Errors,
            error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail));
    }
}
