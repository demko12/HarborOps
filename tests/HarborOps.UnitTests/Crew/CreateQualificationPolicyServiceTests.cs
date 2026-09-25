using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class CreateQualificationPolicyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_persists_the_policy_requirements_and_a_created_history_entry()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).CreateAsync(actorUserId, ValidRequest(position.Id), CancellationToken.None);

        var saved = await dbContext.Set<CrewQualificationPolicy>().SingleAsync(p => p.Id == response.Id);
        Assert.Equal(position.Id, saved.CrewPositionId);
        Assert.True(saved.Enabled);
        Assert.Equal(actorUserId, saved.CreatedByUserId);

        var requirements = await dbContext.Set<CrewQualificationRequirement>()
            .Where(r => r.PolicyId == response.Id).ToListAsync();
        Assert.Single(requirements);
        Assert.Equal("STCW-II/1", requirements[0].RequiredType);

        var history = await dbContext.Set<CrewQualificationPolicyHistory>().SingleAsync(h => h.PolicyId == response.Id);
        Assert.Equal(CrewQualificationPolicyHistory.CreatedEvent, history.EventType);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.True(history.NewEnabled);
    }

    [Fact]
    public async Task CreateAsync_allows_an_explicitly_empty_requirement_set()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);

        var response = await CreateService(dbContext).CreateAsync(
            Guid.NewGuid(), ValidRequest(position.Id) with { Requirements = [] }, CancellationToken.None);

        Assert.Empty(response.RequiredTypes);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_crew_position()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();

        await Assert.ThrowsAsync<CrewPositionNotFoundException>(() => CreateService(dbContext).CreateAsync(
            Guid.NewGuid(), ValidRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_policy_for_the_same_rank()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);

        await Assert.ThrowsAsync<DuplicateCrewQualificationPolicyException>(() => CreateService(dbContext).CreateAsync(
            Guid.NewGuid(), ValidRequest(position.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_required_types()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);

        var request = ValidRequest(position.Id) with
        {
            Requirements =
            [
                new QualificationRequirementInput { RequiredType = "STCW-II/1" },
                new QualificationRequirementInput { RequiredType = "stcw-ii/1" },
            ],
        };

        var exception = await Assert.ThrowsAsync<CreateQualificationPolicyValidationException>(
            () => CreateService(dbContext).CreateAsync(Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(QualificationRequirementValidation.RequirementsField, exception.Errors);
    }

    private static CreateQualificationPolicyService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));

    private static CreateQualificationPolicyRequest ValidRequest(Guid crewPositionId) => new()
    {
        CrewPositionId = crewPositionId,
        Enabled = true,
        Requirements = [new QualificationRequirementInput { RequiredType = "STCW-II/1" }],
    };
}
