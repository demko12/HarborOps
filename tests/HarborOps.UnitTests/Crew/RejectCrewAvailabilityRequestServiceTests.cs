using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RejectCrewAvailabilityRequestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RejectAsync_moves_to_rejected_and_records_the_reason()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));

        var response = await CreateService(dbContext).RejectAsync(
            request.Id, Guid.NewGuid(), new RejectCrewAvailabilityRequestRequest { Reason = "No cover." }, CancellationToken.None);

        Assert.Equal(CrewAvailabilityRequest.RejectedStatus, response.NewStatus);
        Assert.Equal("No cover.", response.Reason);

        var transition = await dbContext.Set<CrewAvailabilityTransition>()
            .SingleAsync(t => t.AvailabilityRequestId == request.Id && t.EventType == CrewAvailabilityTransition.RejectedEvent);
        Assert.Equal("No cover.", transition.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAsync_requires_a_reason(string? reason)
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));

        await Assert.ThrowsAsync<RejectCrewAvailabilityRequestValidationException>(
            () => CreateService(dbContext).RejectAsync(
                request.Id, Guid.NewGuid(), new RejectCrewAvailabilityRequestRequest { Reason = reason }, CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_throws_for_an_unknown_request()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();

        await Assert.ThrowsAsync<CrewAvailabilityRequestNotFoundException>(
            () => CreateService(dbContext).RejectAsync(
                Guid.NewGuid(), Guid.NewGuid(), new RejectCrewAvailabilityRequestRequest { Reason = "x" }, CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_throws_once_the_request_is_approved()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), CrewAvailabilityRequest.ApprovedStatus);

        await Assert.ThrowsAsync<InvalidCrewAvailabilityTransitionException>(
            () => CreateService(dbContext).RejectAsync(
                request.Id, Guid.NewGuid(), new RejectCrewAvailabilityRequestRequest { Reason = "Too late." }, CancellationToken.None));
    }

    private static RejectCrewAvailabilityRequestService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
