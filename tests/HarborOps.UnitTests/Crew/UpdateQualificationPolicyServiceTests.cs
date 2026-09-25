using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class UpdateQualificationPolicyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateAsync_replaces_requirements_and_bumps_the_concurrency_token()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var policy = await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1", "STCW-VI/1"]);
        var previousToken = policy.ConcurrencyToken;
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).UpdateAsync(
            policy.Id, actorUserId,
            new UpdateQualificationPolicyRequest
            {
                Enabled = true,
                Requirements = [new QualificationRequirementInput { RequiredType = "STCW-VI/2" }],
            },
            CancellationToken.None);

        Assert.Equal(["STCW-VI/2"], response.RequiredTypes);

        var savedPolicy = await dbContext.Set<CrewQualificationPolicy>().SingleAsync(p => p.Id == policy.Id);
        Assert.NotEqual(previousToken, savedPolicy.ConcurrencyToken);

        var history = await dbContext.Set<CrewQualificationPolicyHistory>().SingleAsync(h => h.PolicyId == policy.Id);
        Assert.Equal(CrewQualificationPolicyHistory.UpdatedEvent, history.EventType);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.NotNull(history.PreviousRequiredTypesSnapshot);
        Assert.Contains("STCW-II/1", history.PreviousRequiredTypesSnapshot);
        Assert.NotNull(history.NewRequiredTypesSnapshot);
        Assert.Contains("STCW-VI/2", history.NewRequiredTypesSnapshot);
    }

    [Fact]
    public async Task UpdateAsync_toggles_enabled()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var policy = await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);

        var response = await CreateService(dbContext).UpdateAsync(
            policy.Id, Guid.NewGuid(),
            new UpdateQualificationPolicyRequest
            {
                Enabled = false,
                Requirements = [new QualificationRequirementInput { RequiredType = "STCW-II/1" }],
            },
            CancellationToken.None);

        Assert.False(response.Enabled);

        var history = await dbContext.Set<CrewQualificationPolicyHistory>().SingleAsync(h => h.PolicyId == policy.Id);
        Assert.True(history.PreviousEnabled);
        Assert.False(history.NewEnabled);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_missing_policy()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();

        await Assert.ThrowsAsync<CrewQualificationPolicyNotFoundException>(() => CreateService(dbContext).UpdateAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new UpdateQualificationPolicyRequest { Enabled = true, Requirements = [] },
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_duplicate_required_types()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var policy = await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);

        var request = new UpdateQualificationPolicyRequest
        {
            Enabled = true,
            Requirements =
            [
                new QualificationRequirementInput { RequiredType = "STCW-II/1" },
                new QualificationRequirementInput { RequiredType = "STCW-II/1" },
            ],
        };

        await Assert.ThrowsAsync<UpdateQualificationPolicyValidationException>(
            () => CreateService(dbContext).UpdateAsync(policy.Id, Guid.NewGuid(), request, CancellationToken.None));
    }

    private static UpdateQualificationPolicyService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
