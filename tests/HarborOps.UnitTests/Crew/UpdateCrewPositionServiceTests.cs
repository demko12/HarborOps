using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class UpdateCrewPositionServiceTests
{
    [Fact]
    public async Task UpdateAsync_updates_the_position_and_persists_it()
    {
        await using var dbContext = CreateDbContext();
        var positionId = await SeedPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        var service = new UpdateCrewPositionService(dbContext);
        var request = CreateValidRequest() with { RankName = "Chief Bosun", Department = CrewPositionDepartments.Engine };

        var response = await service.UpdateAsync(positionId, request, CancellationToken.None);

        Assert.Equal(positionId, response.Id);
        var updated = await dbContext.Set<CrewPosition>().SingleAsync(p => p.Id == positionId, CancellationToken.None);
        Assert.Equal("Chief Bosun", updated.RankName);
        Assert.Equal(CrewPositionDepartments.Engine, updated.Department);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unknown_position()
    {
        await using var dbContext = CreateDbContext();
        var service = new UpdateCrewPositionService(dbContext);
        var request = CreateValidRequest();

        await Assert.ThrowsAsync<CrewPositionNotFoundException>(
            () => service.UpdateAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_invalid_requests_before_saving()
    {
        await using var dbContext = CreateDbContext();
        var positionId = await SeedPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        var service = new UpdateCrewPositionService(dbContext);
        var request = CreateValidRequest() with { RankName = " " };

        await Assert.ThrowsAsync<UpdateCrewPositionValidationException>(
            () => service.UpdateAsync(positionId, request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_allows_saving_the_position_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var positionId = await SeedPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        var service = new UpdateCrewPositionService(dbContext);
        var request = CreateValidRequest();

        var response = await service.UpdateAsync(positionId, request, CancellationToken.None);

        Assert.Equal(positionId, response.Id);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_duplicate_rank_and_department_from_another_position()
    {
        await using var dbContext = CreateDbContext();
        await SeedPositionAsync(dbContext, "Chief Engineer", CrewPositionDepartments.Engine);
        var positionId = await SeedPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        var service = new UpdateCrewPositionService(dbContext);
        var request = CreateValidRequest() with { RankName = "Chief Engineer", Department = CrewPositionDepartments.Engine };

        await Assert.ThrowsAsync<DuplicateCrewPositionException>(
            () => service.UpdateAsync(positionId, request, CancellationToken.None));
    }

    private static UpdateCrewPositionRequest CreateValidRequest()
    {
        return new UpdateCrewPositionRequest
        {
            RankName = "Bosun",
            Department = CrewPositionDepartments.Deck,
            SeniorityLevel = CrewPositionSeniorityLevels.SeniorRating,
        };
    }

    private static async Task<Guid> SeedPositionAsync(HarborOpsDbContext dbContext, string rankName, string department)
    {
        var position = CrewPosition.Create(rankName, department, CrewPositionSeniorityLevels.SeniorRating);
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
