using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class CreateVesselManningPlanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_persists_the_plan_lines_and_a_created_history_entry()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext, vesselId).CreateAsync(
            actorUserId, ValidRequest(vesselId, position.Id), CancellationToken.None);

        var saved = await dbContext.Set<VesselManningPlan>().SingleAsync(p => p.Id == response.Id);
        Assert.Equal(vesselId, saved.VesselId);
        Assert.Equal(actorUserId, saved.CreatedByUserId);

        var lines = await dbContext.Set<VesselManningPlanLine>()
            .Where(l => l.VesselManningPlanId == response.Id)
            .ToListAsync();
        Assert.Single(lines);
        Assert.Equal(position.Id, lines[0].RankId);
        Assert.Equal(4, lines[0].RequiredHeadcount);

        var history = await dbContext.Set<VesselManningPlanHistory>()
            .SingleAsync(h => h.VesselManningPlanId == response.Id);
        Assert.Equal(VesselManningPlanHistory.CreatedEvent, history.EventType);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.Null(history.Reason);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_vessel()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var vesselId = Guid.NewGuid();

        var service = new CreateVesselManningPlanService(
            dbContext, new FakeVesselExistenceLookup(null), new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<VesselNotFoundException>(() => service.CreateAsync(
            Guid.NewGuid(), ValidRequest(vesselId, position.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_rank()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var vesselId = Guid.NewGuid();

        await Assert.ThrowsAsync<CrewPositionNotFoundException>(() => CreateService(dbContext, vesselId).CreateAsync(
            Guid.NewGuid(), ValidRequest(vesselId, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_rank_lines()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var vesselId = Guid.NewGuid();

        var request = ValidRequest(vesselId, position.Id) with
        {
            Lines =
            [
                new ManningPlanLineInput { RankId = position.Id, RequiredHeadcount = 2 },
                new ManningPlanLineInput { RankId = position.Id, RequiredHeadcount = 3 },
            ],
        };

        var exception = await Assert.ThrowsAsync<CreateVesselManningPlanValidationException>(
            () => CreateService(dbContext, vesselId).CreateAsync(Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(ManningPlanLineValidation.LinesField, exception.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_rejects_a_non_positive_required_headcount(int requiredHeadcount)
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var vesselId = Guid.NewGuid();

        var request = ValidRequest(vesselId, position.Id) with
        {
            Lines = [new ManningPlanLineInput { RankId = position.Id, RequiredHeadcount = requiredHeadcount }],
        };

        await Assert.ThrowsAsync<CreateVesselManningPlanValidationException>(
            () => CreateService(dbContext, vesselId).CreateAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_end_date_before_the_start_date()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var vesselId = Guid.NewGuid();

        var request = ValidRequest(vesselId, position.Id) with
        {
            EffectiveStartDate = new DateOnly(2026, 3, 31),
            EffectiveEndDate = new DateOnly(2026, 1, 1),
        };

        await Assert.ThrowsAsync<CreateVesselManningPlanValidationException>(
            () => CreateService(dbContext, vesselId).CreateAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Theory]
    [InlineData(2026, 2, 1, 2026, 2, 28, true)] // touches existing start
    [InlineData(2026, 1, 1, 2026, 1, 15, true)] // touches existing end
    [InlineData(2026, 1, 20, 2026, 2, 5, true)] // overlaps the middle
    [InlineData(2026, 2, 16, 2026, 3, 1, false)] // strictly after
    [InlineData(2025, 12, 1, 2025, 12, 31, false)] // strictly before
    public async Task CreateAsync_applies_inclusive_overlap_boundaries(
        int startYear, int startMonth, int startDay,
        int endYear, int endMonth, int endDay,
        bool expectOverlap)
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var vesselId = Guid.NewGuid();
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 1), [(position.Id, 1)]);

        var request = ValidRequest(vesselId, position.Id) with
        {
            EffectiveStartDate = new DateOnly(startYear, startMonth, startDay),
            EffectiveEndDate = new DateOnly(endYear, endMonth, endDay),
        };

        if (expectOverlap)
        {
            await Assert.ThrowsAsync<OverlappingVesselManningPlanException>(
                () => CreateService(dbContext, vesselId).CreateAsync(Guid.NewGuid(), request, CancellationToken.None));
        }
        else
        {
            var response = await CreateService(dbContext, vesselId).CreateAsync(
                Guid.NewGuid(), request, CancellationToken.None);
            Assert.NotEqual(Guid.Empty, response.Id);
        }
    }

    private static CreateVesselManningPlanService CreateService(HarborOpsDbContext dbContext, Guid vesselId) =>
        new(dbContext, new FakeVesselExistenceLookup(new VesselExistenceResult(vesselId, "MV Test")), new FixedTimeProvider(Now));

    private static CreateVesselManningPlanRequest ValidRequest(Guid vesselId, Guid rankId) => new()
    {
        VesselId = vesselId,
        EffectiveStartDate = new DateOnly(2026, 1, 1),
        EffectiveEndDate = new DateOnly(2026, 3, 31),
        Lines = [new ManningPlanLineInput { RankId = rankId, RequiredHeadcount = 4 }],
    };
}
