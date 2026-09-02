using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class UpdateCrewRotationServiceTests
{
    [Fact]
    public async Task UpdateAsync_replaces_the_rotation_pattern_and_planned_relief_date()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
        var service = new UpdateCrewRotationService(dbContext);

        var response = await service.UpdateAsync(
            assignment.Id,
            new UpdateCrewRotationRequest { RotationPattern = "4-4", PlannedReliefDate = new DateOnly(2026, 8, 1) },
            CancellationToken.None);

        var rotation = await dbContext.Set<CrewRotation>().SingleAsync(r => r.Id == response.Id);
        Assert.Equal("4-4", rotation.RotationPattern);
        Assert.Equal(new DateOnly(2026, 8, 1), rotation.PlannedReliefDate);
    }

    [Fact]
    public async Task UpdateAsync_throws_when_assignment_not_found()
    {
        await using var dbContext = CreateDbContext();
        var service = new UpdateCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotFoundException>(
            () => service.UpdateAsync(
                Guid.NewGuid(),
                new UpdateCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
                CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.AssignmentId);
    }

    [Fact]
    public async Task UpdateAsync_throws_when_no_rotation_is_planned()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new UpdateCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewRotationNotFoundException>(
            () => service.UpdateAsync(
                assignment.Id,
                new UpdateCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
                CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task UpdateAsync_throws_validation_when_planned_relief_date_precedes_sign_on_date()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
        var service = new UpdateCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewRotationValidationException>(
            () => service.UpdateAsync(
                assignment.Id,
                new UpdateCrewRotationRequest
                {
                    RotationPattern = "2-2",
                    PlannedReliefDate = assignment.SignOnDate.AddDays(-1),
                },
                CancellationToken.None));

        Assert.Contains(CrewRotationValidator.PlannedReliefDateField, exception.Errors);
    }

    [Fact]
    public async Task UpdateAsync_throws_when_assignment_is_already_signed_off()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1)));
        assignment.SignOff(new DateOnly(2026, 6, 1));
        await dbContext.SaveChangesAsync();
        var service = new UpdateCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotEligibleForRotationException>(
            () => service.UpdateAsync(
                assignment.Id,
                new UpdateCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
                CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task UpdateAsync_can_clear_a_previously_selected_relieving_member()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var relievingMember = await SeedCrewMemberAsync(dbContext, assignment.RankId, "Relief Member");
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, relievingMember.Id, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
        var service = new UpdateCrewRotationService(dbContext);

        var response = await service.UpdateAsync(
            assignment.Id,
            new UpdateCrewRotationRequest
            {
                RelievingCrewMemberId = null,
                RotationPattern = "2-2",
                PlannedReliefDate = new DateOnly(2026, 7, 1),
            },
            CancellationToken.None);

        var rotation = await dbContext.Set<CrewRotation>().SingleAsync(r => r.Id == response.Id);
        Assert.Null(rotation.RelievingCrewMemberId);
    }

    [Fact]
    public async Task UpdateAsync_throws_when_the_relieving_member_is_the_assigned_member()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
        var service = new UpdateCrewRotationService(dbContext);

        var exception = await Assert.ThrowsAsync<SelfRelievingCrewMemberException>(
            () => service.UpdateAsync(
                assignment.Id,
                new UpdateCrewRotationRequest
                {
                    RelievingCrewMemberId = assignment.CrewMemberId,
                    RotationPattern = "2-2",
                    PlannedReliefDate = new DateOnly(2026, 7, 1),
                },
                CancellationToken.None));

        Assert.Equal(assignment.CrewMemberId, exception.CrewMemberId);
    }

    private static async Task<CrewMember> SeedCrewMemberAsync(HarborOpsDbContext dbContext, Guid baseRankId, string name)
    {
        var nameParts = name.Split(' ', 2);
        var member = CrewMember.Create(
            baseRankId, nameParts[0], nameParts.Length > 1 ? nameParts[1] : string.Empty, new DateOnly(1990, 1, 1), "Montenegro",
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
