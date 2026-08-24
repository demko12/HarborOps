using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class PlanCrewRotationServiceTests
{
    [Fact]
    public async Task PlanAsync_creates_a_rotation_without_a_relieving_member()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new PlanCrewRotationService(dbContext);

        var response = await service.PlanAsync(
            assignment.Id,
            new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        var rotation = await dbContext.Set<CrewRotation>().SingleAsync(r => r.Id == response.Id);
        Assert.Equal(assignment.Id, rotation.AssignmentId);
        Assert.Null(rotation.RelievingCrewMemberId);
        Assert.Equal("2-2", rotation.RotationPattern);
        Assert.Equal(new DateOnly(2026, 7, 1), rotation.PlannedReliefDate);
    }

    [Fact]
    public async Task PlanAsync_creates_a_rotation_with_a_relieving_member()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var relievingMember = await SeedCrewMemberAsync(dbContext, assignment.RankId, "Relief Member");
        var service = new PlanCrewRotationService(dbContext);

        var response = await service.PlanAsync(
            assignment.Id,
            new PlanCrewRotationRequest
            {
                RelievingCrewMemberId = relievingMember.Id,
                RotationPattern = "2-2",
                PlannedReliefDate = new DateOnly(2026, 7, 1),
            },
            CancellationToken.None);

        var rotation = await dbContext.Set<CrewRotation>().SingleAsync(r => r.Id == response.Id);
        Assert.Equal(relievingMember.Id, rotation.RelievingCrewMemberId);
    }

    [Fact]
    public async Task PlanAsync_throws_when_assignment_not_found()
    {
        await using var dbContext = CreateDbContext();
        var service = new PlanCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotFoundException>(
            () => service.PlanAsync(
                Guid.NewGuid(),
                new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
                CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.AssignmentId);
    }

    [Fact]
    public async Task PlanAsync_throws_validation_when_rotation_pattern_missing()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new PlanCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewRotationValidationException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest { PlannedReliefDate = new DateOnly(2026, 7, 1) },
                CancellationToken.None));

        Assert.Contains(CrewRotationValidator.RotationPatternField, exception.Errors);
    }

    [Fact]
    public async Task PlanAsync_throws_validation_when_planned_relief_date_precedes_sign_on_date()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new PlanCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewRotationValidationException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest
                {
                    RotationPattern = "2-2",
                    PlannedReliefDate = assignment.SignOnDate.AddDays(-1),
                },
                CancellationToken.None));

        Assert.Contains(CrewRotationValidator.PlannedReliefDateField, exception.Errors);
    }

    [Fact]
    public async Task PlanAsync_throws_when_assignment_is_already_signed_off()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        assignment.SignOff(new DateOnly(2026, 6, 1));
        await dbContext.SaveChangesAsync();
        var service = new PlanCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotEligibleForRotationException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
                CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task PlanAsync_throws_when_a_rotation_already_exists_for_the_assignment()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
        var service = new PlanCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<DuplicateCrewRotationException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest { RotationPattern = "4-4", PlannedReliefDate = new DateOnly(2026, 8, 1) },
                CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task PlanAsync_throws_when_the_relieving_member_is_the_assigned_member()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new PlanCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<SelfRelievingCrewMemberException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest
                {
                    RelievingCrewMemberId = assignment.CrewMemberId,
                    RotationPattern = "2-2",
                    PlannedReliefDate = new DateOnly(2026, 7, 1),
                },
                CancellationToken.None));

        Assert.Equal(assignment.CrewMemberId, exception.CrewMemberId);
    }

    [Fact]
    public async Task PlanAsync_throws_when_the_relieving_member_does_not_exist()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new PlanCrewRotationService(dbContext);
        var missingMemberId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest
                {
                    RelievingCrewMemberId = missingMemberId,
                    RotationPattern = "2-2",
                    PlannedReliefDate = new DateOnly(2026, 7, 1),
                },
                CancellationToken.None));

        Assert.Equal(missingMemberId, exception.CrewMemberId);
    }

    [Fact]
    public async Task PlanAsync_throws_when_the_relieving_member_already_has_an_open_assignment()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var relievingMember = await SeedCrewMemberAsync(dbContext, assignment.RankId, "Relief Member");
        var relievingMemberAssignment = CrewAssignment.CreatePlanned(
            relievingMember.Id, Guid.NewGuid(), assignment.RankId, null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await dbContext.Set<CrewAssignment>().AddAsync(relievingMemberAssignment);
        await dbContext.SaveChangesAsync();
        var service = new PlanCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<RelievingCrewMemberUnavailableException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest
                {
                    RelievingCrewMemberId = relievingMember.Id,
                    RotationPattern = "2-2",
                    PlannedReliefDate = new DateOnly(2026, 7, 1),
                },
                CancellationToken.None));

        Assert.Equal(relievingMember.Id, exception.CrewMemberId);
    }

    [Fact]
    public async Task PlanAsync_does_not_persist_anything_when_a_guard_fails()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new PlanCrewRotationService(dbContext);

        await Assert.ThrowsAsync<SelfRelievingCrewMemberException>(
            () => service.PlanAsync(
                assignment.Id,
                new PlanCrewRotationRequest
                {
                    RelievingCrewMemberId = assignment.CrewMemberId,
                    RotationPattern = "2-2",
                    PlannedReliefDate = new DateOnly(2026, 7, 1),
                },
                CancellationToken.None));

        Assert.False(await dbContext.Set<CrewRotation>().AnyAsync());
    }

    private static async Task<CrewMember> SeedCrewMemberAsync(HarborOpsDbContext dbContext, Guid baseRankId, string name)
    {
        var member = CrewMember.Create(
            baseRankId, name, new DateOnly(1990, 1, 1), "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..10], "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
        return member;
    }

    private static async Task<CrewAssignment> SeedAssignmentAsync(HarborOpsDbContext dbContext)
    {
        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);

        var member = await SeedCrewMemberAsync(dbContext, position.Id, "Marko Petrovic");

        var assignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
