using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class ExecuteCrewRotationServiceTests
{
    private static readonly DateTime Today = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteAsync_signs_off_the_original_assignment_and_activates_the_replacement()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, relievingMember) = await SeedRotationReadyForExecutionAsync(dbContext);
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var response = await service.ExecuteAsync(assignment.Id, CancellationToken.None);

        var original = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(DateOnly.FromDateTime(Today), original.ActualSignOffDate);

        var replacement = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == response.ReplacementAssignmentId);
        Assert.Equal(relievingMember.Id, replacement.CrewMemberId);
        Assert.Equal(assignment.VesselId, replacement.VesselId);
        Assert.Equal(assignment.RankId, replacement.RankId);
        Assert.Equal(CrewAssignment.ActiveStatus, replacement.Status);
        Assert.Equal(DateOnly.FromDateTime(Today), replacement.SignOnDate);
    }

    [Fact]
    public async Task ExecuteAsync_carries_the_original_tour_length_forward_to_the_replacement()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, _) = await SeedRotationReadyForExecutionAsync(
            dbContext, signOnDate: new DateOnly(2026, 1, 1), plannedSignOffDate: new DateOnly(2026, 6, 30));
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var response = await service.ExecuteAsync(assignment.Id, CancellationToken.None);

        var expectedTourLength = assignment.PlannedSignOffDate.DayNumber - assignment.SignOnDate.DayNumber;
        Assert.Equal(
            DateOnly.FromDateTime(Today).AddDays(expectedTourLength),
            response.ReplacementPlannedSignOffDate);
    }

    [Fact]
    public async Task ExecuteAsync_retains_the_rotation_outcome()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, _) = await SeedRotationReadyForExecutionAsync(dbContext);
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var response = await service.ExecuteAsync(assignment.Id, CancellationToken.None);

        var rotation = await dbContext.Set<CrewRotation>().SingleAsync(r => r.AssignmentId == assignment.Id);
        Assert.Equal(CrewRotation.ExecutedStatus, rotation.Status);
        Assert.Equal(response.ReplacementAssignmentId, rotation.ReplacementAssignmentId);
        Assert.NotNull(rotation.ExecutedAtUtc);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_assignment_not_found()
    {
        await using var dbContext = CreateDbContext();
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotFoundException>(
            () => service.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.AssignmentId);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_no_rotation_is_planned()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<CrewRotationNotFoundException>(
            () => service.ExecuteAsync(assignment.Id, CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_no_relieving_member_is_selected()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<CrewRotationNotExecutableException>(
            () => service.ExecuteAsync(assignment.Id, CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_the_assignment_is_already_signed_off()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, _) = await SeedRotationReadyForExecutionAsync(dbContext);
        assignment.SignOff(new DateOnly(2026, 6, 20));
        await dbContext.SaveChangesAsync();
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotEligibleForRotationException>(
            () => service.ExecuteAsync(assignment.Id, CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_the_relieving_member_now_has_an_open_assignment()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, relievingMember) = await SeedRotationReadyForExecutionAsync(dbContext);
        var conflictingAssignment = CrewAssignment.CreatePlanned(
            relievingMember.Id, Guid.NewGuid(), assignment.RankId, null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await dbContext.Set<CrewAssignment>().AddAsync(conflictingAssignment);
        await dbContext.SaveChangesAsync();
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<RelievingCrewMemberUnavailableException>(
            () => service.ExecuteAsync(assignment.Id, CancellationToken.None));

        Assert.Equal(relievingMember.Id, exception.CrewMemberId);
    }

    [Fact]
    public async Task ExecuteAsync_leaves_nothing_persisted_when_a_guard_fails()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        await Assert.ThrowsAsync<CrewRotationNotExecutableException>(
            () => service.ExecuteAsync(assignment.Id, CancellationToken.None));

        var unchanged = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Null(unchanged.ActualSignOffDate);
        Assert.Equal(1, await dbContext.Set<CrewAssignment>().CountAsync());
        Assert.True(await dbContext.Set<CrewRotation>().AnyAsync(r => r.AssignmentId == assignment.Id));
    }

    [Fact]
    public async Task ExecuteAsync_throws_and_keeps_the_originals_when_the_relieving_member_has_approved_unavailability()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, relievingMember) = await SeedRotationReadyForExecutionAsync(dbContext);
        var unavailability = CrewAvailabilityRequest.Create(
            relievingMember.Id, "SickLeave", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20),
            "Recovering.", null, Guid.NewGuid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        unavailability.Approve(Guid.NewGuid(), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.Set<CrewAvailabilityRequest>().AddAsync(unavailability);
        await dbContext.SaveChangesAsync();
        var service = new ExecuteCrewRotationService(dbContext, new FakeDateTimeProvider(Today));

        await Assert.ThrowsAsync<CrewMemberUnavailableException>(
            () => service.ExecuteAsync(assignment.Id, CancellationToken.None));

        var original = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Null(original.ActualSignOffDate);
        var rotation = await dbContext.Set<CrewRotation>().SingleAsync(r => r.AssignmentId == assignment.Id);
        Assert.Equal(CrewRotation.PlannedStatus, rotation.Status);
        Assert.Equal(0, await dbContext.Set<CrewAssignment>().CountAsync(a => a.CrewMemberId == relievingMember.Id));
    }

    private static async Task<(CrewAssignment Assignment, CrewMember RelievingMember)> SeedRotationReadyForExecutionAsync(
        HarborOpsDbContext dbContext,
        DateOnly? signOnDate = null,
        DateOnly? plannedSignOffDate = null)
    {
        var assignment = await SeedAssignmentAsync(dbContext, signOnDate, plannedSignOffDate);
        var relievingMember = await SeedCrewMemberAsync(dbContext, assignment.RankId, "Relief Member");
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, relievingMember.Id, "2-2", new DateOnly(2026, 6, 15)));
        await dbContext.SaveChangesAsync();

        return (assignment, relievingMember);
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

    private static async Task<CrewAssignment> SeedAssignmentAsync(
        HarborOpsDbContext dbContext,
        DateOnly? signOnDate = null,
        DateOnly? plannedSignOffDate = null)
    {
        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);

        var member = await SeedCrewMemberAsync(dbContext, position.Id, "Marko Petrovic");

        var assignment = CrewAssignment.CreatePlanned(
            member.Id,
            Guid.NewGuid(),
            position.Id,
            null,
            signOnDate ?? new DateOnly(2026, 1, 1),
            plannedSignOffDate ?? new DateOnly(2026, 12, 31));
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
