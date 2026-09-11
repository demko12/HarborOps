using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class ApproveCrewAvailabilityRequestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApproveAsync_moves_to_approved_and_records_a_transition()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));
        var reviewer = Guid.NewGuid();

        var response = await CreateService(dbContext).ApproveAsync(request.Id, reviewer, CancellationToken.None);

        Assert.Equal(CrewAvailabilityRequest.RequestedStatus, response.PreviousStatus);
        Assert.Equal(CrewAvailabilityRequest.ApprovedStatus, response.NewStatus);
        Assert.Equal(reviewer, response.ActorUserId);

        var saved = await dbContext.Set<CrewAvailabilityRequest>().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(CrewAvailabilityRequest.ApprovedStatus, saved.Status);

        var transition = await dbContext.Set<CrewAvailabilityTransition>()
            .SingleAsync(t => t.AvailabilityRequestId == request.Id && t.EventType == CrewAvailabilityTransition.ApprovedEvent);
        Assert.Equal(reviewer, transition.ActorUserId);
    }

    [Fact]
    public async Task ApproveAsync_rejects_approval_by_the_requester()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var requester = Guid.NewGuid();
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), requestedByUserId: requester);

        await Assert.ThrowsAsync<CrewAvailabilitySelfApprovalException>(
            () => CreateService(dbContext).ApproveAsync(request.Id, requester, CancellationToken.None));

        var saved = await dbContext.Set<CrewAvailabilityRequest>().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(CrewAvailabilityRequest.RequestedStatus, saved.Status);
    }

    [Fact]
    public async Task ApproveAsync_throws_when_the_request_is_no_longer_pending()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));
        await CreateService(dbContext).ApproveAsync(request.Id, Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidCrewAvailabilityTransitionException>(
            () => CreateService(dbContext).ApproveAsync(request.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData(false, "PlannedAssignment")]
    [InlineData(true, "ActiveAssignment")]
    public async Task ApproveAsync_rejects_when_the_period_overlaps_an_assignment(bool active, string expectedRecordType)
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        await CrewAvailabilityTestData.SeedAssignmentAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 9, 5), active);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));

        var exception = await Assert.ThrowsAsync<CrewAvailabilityApprovalConflictException>(
            () => CreateService(dbContext).ApproveAsync(request.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(expectedRecordType, exception.RecordType);
        Assert.DoesNotContain("Seeded private notes", exception.Message, StringComparison.Ordinal);

        var savedAssignment = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.CrewMemberId == member.Id);
        Assert.Equal(active ? CrewAssignment.ActiveStatus : CrewAssignment.PlannedStatus, savedAssignment.Status);
        Assert.Equal(0, await dbContext.Set<CrewAvailabilityTransition>()
            .CountAsync(t => t.EventType == CrewAvailabilityTransition.ApprovedEvent));
    }

    [Fact]
    public async Task ApproveAsync_rejects_when_the_member_is_a_planned_relief()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var otherMember = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var assignment = await CrewAvailabilityTestData.SeedAssignmentAsync(
            dbContext, otherMember.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var rotation = CrewRotation.Create(assignment.Id, member.Id, "2-2", new DateOnly(2026, 8, 6));
        await dbContext.Set<CrewRotation>().AddAsync(rotation);
        await dbContext.SaveChangesAsync();

        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));

        var exception = await Assert.ThrowsAsync<CrewAvailabilityApprovalConflictException>(
            () => CreateService(dbContext).ApproveAsync(request.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("PlannedReliefRotation", exception.RecordType);
    }

    private static ApproveCrewAvailabilityRequestService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
