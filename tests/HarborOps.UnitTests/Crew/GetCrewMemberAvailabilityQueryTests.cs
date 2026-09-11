using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewMemberAvailabilityQueryTests
{
    private static readonly DateTime Today = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_returns_the_members_periods_with_history_newest_first()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);

        var approve = new ApproveCrewAvailabilityRequestService(dbContext, new FixedTimeProvider(new DateTimeOffset(Today.AddMinutes(5))));
        var requestService = new RequestCrewAvailabilityService(dbContext, new FixedTimeProvider(new DateTimeOffset(Today)));

        var created = await requestService.RequestAsync(
            member.Id,
            Guid.NewGuid(),
            new Modules.Crew.Contracts.CreateCrewAvailabilityRequestRequest
            {
                Type = "Training",
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 10),
                Reason = "STCW refresher.",
            },
            CancellationToken.None);
        await approve.ApproveAsync(created.Id, Guid.NewGuid(), CancellationToken.None);

        var query = new GetCrewMemberAvailabilityQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(CrewAvailabilityRequest.ApprovedStatus, item.Status);
        Assert.Equal(2, item.History.Count);
        Assert.Equal(CrewAvailabilityTransition.ApprovedEvent, item.History[0].EventType);
        Assert.Equal(CrewAvailabilityTransition.RequestedEvent, item.History[1].EventType);
    }

    [Fact]
    public async Task GetAsync_returns_an_empty_list_for_a_member_with_no_periods()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var query = new GetCrewMemberAvailabilityQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetAsync_throws_for_an_unknown_member()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var query = new GetCrewMemberAvailabilityQuery(dbContext, new FakeDateTimeProvider(Today));

        await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
