using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RegisterCrewPositionServiceTests
{
    [Fact]
    public async Task RegisterAsync_persists_a_new_position()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCrewPositionService(dbContext);
        var request = CreateValidRequest();

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var saved = await dbContext.Set<CrewPosition>().SingleAsync(p => p.Id == response.Id, CancellationToken.None);
        Assert.Equal("Bosun", saved.RankName);
        Assert.Equal(CrewPositionDepartments.Deck, saved.Department);
        Assert.Equal(CrewPositionSeniorityLevels.SeniorRating, saved.SeniorityLevel);
    }

    [Fact]
    public async Task RegisterAsync_trims_whitespace_before_persisting()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCrewPositionService(dbContext);
        var request = CreateValidRequest() with { RankName = "  Bosun  " };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var saved = await dbContext.Set<CrewPosition>().SingleAsync(p => p.Id == response.Id, CancellationToken.None);
        Assert.Equal("Bosun", saved.RankName);
    }

    [Fact]
    public async Task RegisterAsync_rejects_invalid_requests_before_saving()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCrewPositionService(dbContext);
        var request = CreateValidRequest() with { RankName = " " };

        await Assert.ThrowsAsync<RegisterCrewPositionValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Empty(await dbContext.Set<CrewPosition>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_duplicate_rank_and_department_regardless_of_case_and_whitespace()
    {
        await using var dbContext = CreateDbContext();
        await SeedPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        var service = new RegisterCrewPositionService(dbContext);
        var request = CreateValidRequest() with { RankName = "  bosun  " };

        var exception = await Assert.ThrowsAsync<DuplicateCrewPositionException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal("bosun", exception.RankName);
        Assert.Equal(CrewPositionDepartments.Deck, exception.Department);
    }

    [Fact]
    public async Task RegisterAsync_allows_the_same_rank_name_in_a_different_department()
    {
        await using var dbContext = CreateDbContext();
        await SeedPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        var service = new RegisterCrewPositionService(dbContext);
        var request = CreateValidRequest() with { Department = CrewPositionDepartments.Engine };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var saved = await dbContext.Set<CrewPosition>().SingleAsync(p => p.Id == response.Id, CancellationToken.None);
        Assert.Equal(CrewPositionDepartments.Engine, saved.Department);
    }

    private static RegisterCrewPositionRequest CreateValidRequest()
    {
        return new RegisterCrewPositionRequest
        {
            RankName = "Bosun",
            Department = CrewPositionDepartments.Deck,
            SeniorityLevel = CrewPositionSeniorityLevels.SeniorRating,
        };
    }

    private static async Task SeedPositionAsync(HarborOpsDbContext dbContext, string rankName, string department)
    {
        var position = CrewPosition.Create(rankName, department, CrewPositionSeniorityLevels.SeniorRating);
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
