using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class ReplaceVesselManningPlanLinesServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReplaceAsync_adds_updates_and_removes_lines_and_bumps_the_concurrency_token()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var cook = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Cook");
        var engineer = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Engineer");
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31),
            [(bosun.Id, 2), (cook.Id, 1)]);
        var previousToken = plan.ConcurrencyToken;
        var actorUserId = Guid.NewGuid();

        var request = new ReplaceVesselManningPlanLinesRequest
        {
            Reason = "Reassessed staffing needs.",
            Lines =
            [
                new ManningPlanLineInput { RankId = bosun.Id, RequiredHeadcount = 3 }, // updated
                new ManningPlanLineInput { RankId = engineer.Id, RequiredHeadcount = 1 }, // added
                // cook removed
            ],
        };

        var response = await CreateService(dbContext).ReplaceAsync(plan.Id, actorUserId, request, CancellationToken.None);

        Assert.Equal(2, response.Lines.Count);
        Assert.Contains(response.Lines, l => l.RankId == bosun.Id && l.RequiredHeadcount == 3);
        Assert.Contains(response.Lines, l => l.RankId == engineer.Id && l.RequiredHeadcount == 1);
        Assert.DoesNotContain(response.Lines, l => l.RankId == cook.Id);

        var savedPlan = await dbContext.Set<VesselManningPlan>().SingleAsync(p => p.Id == plan.Id);
        Assert.NotEqual(previousToken, savedPlan.ConcurrencyToken);

        var history = await dbContext.Set<VesselManningPlanHistory>()
            .SingleAsync(h => h.VesselManningPlanId == plan.Id);
        Assert.Equal(VesselManningPlanHistory.LinesReplacedEvent, history.EventType);
        Assert.Equal("Reassessed staffing needs.", history.Reason);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.NotNull(history.PreviousLinesSnapshot);
        Assert.NotNull(history.NewLinesSnapshot);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_a_missing_plan()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);

        var request = ValidRequest(position.Id);

        await Assert.ThrowsAsync<VesselManningPlanNotFoundException>(
            () => CreateService(dbContext).ReplaceAsync(Guid.NewGuid(), Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceAsync_rejects_an_unknown_rank()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), [(position.Id, 1)]);

        var request = ValidRequest(Guid.NewGuid());

        await Assert.ThrowsAsync<CrewPositionNotFoundException>(
            () => CreateService(dbContext).ReplaceAsync(plan.Id, Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceAsync_requires_a_reason()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), [(position.Id, 1)]);

        var request = ValidRequest(position.Id) with { Reason = null };

        var exception = await Assert.ThrowsAsync<ReplaceVesselManningPlanLinesValidationException>(
            () => CreateService(dbContext).ReplaceAsync(plan.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(ReplaceVesselManningPlanLinesValidator.ReasonField, exception.Errors);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_duplicate_rank_lines()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), [(position.Id, 1)]);

        var request = ValidRequest(position.Id) with
        {
            Lines =
            [
                new ManningPlanLineInput { RankId = position.Id, RequiredHeadcount = 1 },
                new ManningPlanLineInput { RankId = position.Id, RequiredHeadcount = 2 },
            ],
        };

        await Assert.ThrowsAsync<ReplaceVesselManningPlanLinesValidationException>(
            () => CreateService(dbContext).ReplaceAsync(plan.Id, Guid.NewGuid(), request, CancellationToken.None));
    }

    private static ReplaceVesselManningPlanLinesService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));

    private static ReplaceVesselManningPlanLinesRequest ValidRequest(Guid rankId) => new()
    {
        Reason = "Reassessed staffing needs.",
        Lines = [new ManningPlanLineInput { RankId = rankId, RequiredHeadcount = 2 }],
    };
}
