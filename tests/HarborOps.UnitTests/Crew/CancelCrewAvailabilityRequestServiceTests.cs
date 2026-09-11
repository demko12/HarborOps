using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class CancelCrewAvailabilityRequestServiceTests
{
    // Fixed "today" is 15 Jun 2026.
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CancelAsync_cancels_a_pending_request()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));

        var response = await CreateService(dbContext).CancelAsync(
            request.Id, Guid.NewGuid(), new CancelCrewAvailabilityRequestRequest { Reason = "Withdrawn." }, CancellationToken.None);

        Assert.Equal(CrewAvailabilityRequest.CancelledStatus, response.NewStatus);
        Assert.Equal("Withdrawn.", response.Reason);
    }

    [Fact]
    public async Task CancelAsync_cancels_an_approved_period_that_has_not_started()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), CrewAvailabilityRequest.ApprovedStatus);

        var response = await CreateService(dbContext).CancelAsync(
            request.Id, Guid.NewGuid(), new CancelCrewAvailabilityRequestRequest { Reason = "Plans changed." }, CancellationToken.None);

        Assert.Equal(CrewAvailabilityRequest.CancelledStatus, response.NewStatus);
    }

    [Fact]
    public async Task CancelAsync_rejects_an_approved_period_that_has_already_started()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), CrewAvailabilityRequest.ApprovedStatus);

        await Assert.ThrowsAsync<InvalidCrewAvailabilityTransitionException>(
            () => CreateService(dbContext).CancelAsync(
                request.Id, Guid.NewGuid(), new CancelCrewAvailabilityRequestRequest { Reason = "Too late." }, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task CancelAsync_requires_a_reason(string? reason)
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));

        await Assert.ThrowsAsync<CancelCrewAvailabilityRequestValidationException>(
            () => CreateService(dbContext).CancelAsync(
                request.Id, Guid.NewGuid(), new CancelCrewAvailabilityRequestRequest { Reason = reason }, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_throws_once_the_request_is_rejected()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), CrewAvailabilityRequest.RejectedStatus);

        await Assert.ThrowsAsync<InvalidCrewAvailabilityTransitionException>(
            () => CreateService(dbContext).CancelAsync(
                request.Id, Guid.NewGuid(), new CancelCrewAvailabilityRequestRequest { Reason = "x" }, CancellationToken.None));
    }

    private static CancelCrewAvailabilityRequestService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
