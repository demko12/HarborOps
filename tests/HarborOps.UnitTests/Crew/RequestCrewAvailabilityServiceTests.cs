using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RequestCrewAvailabilityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequestAsync_persists_the_request_and_a_requested_transition()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var requester = Guid.NewGuid();

        var response = await CreateService(dbContext).RequestAsync(
            member.Id, requester, ValidRequest(), CancellationToken.None);

        var saved = await dbContext.Set<CrewAvailabilityRequest>().SingleAsync(r => r.Id == response.Id);
        Assert.Equal(CrewAvailabilityRequest.RequestedStatus, saved.Status);
        Assert.Equal(member.Id, saved.CrewMemberId);
        Assert.Equal(requester, saved.RequestedByUserId);
        Assert.Equal(Now.UtcDateTime, saved.RequestedAtUtc);

        var transition = await dbContext.Set<CrewAvailabilityTransition>().SingleAsync(t => t.AvailabilityRequestId == response.Id);
        Assert.Equal(CrewAvailabilityTransition.RequestedEvent, transition.EventType);
        Assert.Equal(requester, transition.ActorUserId);
    }

    [Fact]
    public async Task RequestAsync_rejects_an_unknown_crew_member()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();

        await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => CreateService(dbContext).RequestAsync(Guid.NewGuid(), Guid.NewGuid(), ValidRequest(), CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "reason")]
    [InlineData("NotAType", "reason")]
    [InlineData("AnnualLeave", null)]
    [InlineData("AnnualLeave", "   ")]
    public async Task RequestAsync_rejects_invalid_input(string? type, string? reason)
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = ValidRequest() with { Type = type, Reason = reason };

        await Assert.ThrowsAsync<CreateCrewAvailabilityRequestValidationException>(
            () => CreateService(dbContext).RequestAsync(member.Id, Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_rejects_an_end_date_before_the_start_date()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var request = ValidRequest() with { StartDate = new DateOnly(2026, 8, 10), EndDate = new DateOnly(2026, 8, 1) };

        var exception = await Assert.ThrowsAsync<CreateCrewAvailabilityRequestValidationException>(
            () => CreateService(dbContext).RequestAsync(member.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(RequestCrewAvailabilityValidator.EndDateField, exception.Errors);
    }

    [Theory]
    [InlineData(2026, 8, 15, 2026, 8, 25, CrewAvailabilityRequest.RequestedStatus, true)] // touches existing start
    [InlineData(2026, 8, 30, 2026, 9, 10, CrewAvailabilityRequest.ApprovedStatus, true)] // touches existing end
    [InlineData(2026, 8, 26, 2026, 8, 28, CrewAvailabilityRequest.RequestedStatus, true)] // fully inside
    [InlineData(2026, 7, 1, 2026, 8, 24, CrewAvailabilityRequest.ApprovedStatus, false)] // strictly before
    [InlineData(2026, 8, 31, 2026, 9, 10, CrewAvailabilityRequest.RequestedStatus, false)] // strictly after
    [InlineData(2026, 8, 26, 2026, 8, 28, CrewAvailabilityRequest.RejectedStatus, false)] // overlaps a rejected period -> ignored
    public async Task RequestAsync_applies_inclusive_overlap_boundaries(
        int startYear, int startMonth, int startDay,
        int endYear, int endMonth, int endDay,
        string existingStatus,
        bool expectOverlap)
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 30), existingStatus);

        var request = ValidRequest() with
        {
            StartDate = new DateOnly(startYear, startMonth, startDay),
            EndDate = new DateOnly(endYear, endMonth, endDay),
        };

        if (expectOverlap)
        {
            await Assert.ThrowsAsync<OverlappingCrewAvailabilityRequestException>(
                () => CreateService(dbContext).RequestAsync(member.Id, Guid.NewGuid(), request, CancellationToken.None));
        }
        else
        {
            var response = await CreateService(dbContext).RequestAsync(member.Id, Guid.NewGuid(), request, CancellationToken.None);
            Assert.NotEqual(Guid.Empty, response.Id);
        }
    }

    private static RequestCrewAvailabilityService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));

    private static CreateCrewAvailabilityRequestRequest ValidRequest() => new()
    {
        Type = "AnnualLeave",
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 10),
        Reason = "Family holiday.",
        Notes = "Back on the 11th.",
    };
}
