using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class EndVesselManningPlanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EndAsync_shortens_the_end_date_and_records_history()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), [(position.Id, 1)]);
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).EndAsync(
            plan.Id, actorUserId, new EndVesselManningPlanRequest { NewEndDate = new DateOnly(2026, 2, 15), Reason = "Vessel laid up." },
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 2, 15), response.EffectiveEndDate);

        var history = await dbContext.Set<VesselManningPlanHistory>().SingleAsync(h => h.VesselManningPlanId == plan.Id);
        Assert.Equal(VesselManningPlanHistory.EndDateChangedEvent, history.EventType);
        Assert.Equal("Vessel laid up.", history.Reason);
        Assert.Equal(new DateOnly(2026, 3, 31), history.PreviousEffectiveEndDate);
        Assert.Equal(new DateOnly(2026, 2, 15), history.NewEffectiveEndDate);
        Assert.Equal(actorUserId, history.ActorUserId);
    }

    [Fact]
    public async Task EndAsync_rejects_a_missing_plan()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();

        await Assert.ThrowsAsync<VesselManningPlanNotFoundException>(() => CreateService(dbContext).EndAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new EndVesselManningPlanRequest { NewEndDate = new DateOnly(2026, 2, 15), Reason = "Vessel laid up." },
            CancellationToken.None));
    }

    [Fact]
    public async Task EndAsync_rejects_a_date_after_the_current_end_date()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), [(position.Id, 1)]);

        await Assert.ThrowsAsync<InvalidVesselManningPlanEndDateException>(() => CreateService(dbContext).EndAsync(
            plan.Id, Guid.NewGuid(),
            new EndVesselManningPlanRequest { NewEndDate = new DateOnly(2026, 4, 1), Reason = "Extend." },
            CancellationToken.None));
    }

    [Fact]
    public async Task EndAsync_requires_a_reason()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var plan = await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), [(position.Id, 1)]);

        var exception = await Assert.ThrowsAsync<EndVesselManningPlanValidationException>(() => CreateService(dbContext).EndAsync(
            plan.Id, Guid.NewGuid(), new EndVesselManningPlanRequest { NewEndDate = new DateOnly(2026, 2, 15) },
            CancellationToken.None));

        Assert.Contains(EndVesselManningPlanValidator.ReasonField, exception.Errors);
    }

    private static EndVesselManningPlanService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
