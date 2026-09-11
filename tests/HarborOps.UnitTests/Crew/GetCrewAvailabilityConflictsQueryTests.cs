using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewAvailabilityConflictsQueryTests
{
    [Fact]
    public async Task GetAsync_returns_approved_unavailability_and_assignment_conflicts_without_private_text()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 12), CrewAvailabilityRequest.ApprovedStatus, "SickLeave");
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 12), CrewAvailabilityRequest.RequestedStatus, "AnnualLeave");
        await CrewAvailabilityTestData.SeedAssignmentAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1));

        var query = new GetCrewAvailabilityConflictsQuery(dbContext);

        var response = await query.GetAsync(
            new GetCrewAvailabilityConflictsRequest
            {
                CrewMemberId = member.Id,
                From = new DateOnly(2026, 8, 1),
                To = new DateOnly(2026, 8, 31),
            },
            CancellationToken.None);

        Assert.Equal(2, response.Conflicts.Count);
        Assert.Contains(response.Conflicts, c => c.RecordType == "ApprovedUnavailability" && c.Detail == "SickLeave");
        Assert.Contains(response.Conflicts, c => c.RecordType == "PlannedAssignment");
        Assert.All(response.Conflicts, c => Assert.DoesNotContain("Seeded", c.Detail ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_applies_inclusive_boundaries()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 25), CrewAvailabilityRequest.ApprovedStatus);

        var query = new GetCrewAvailabilityConflictsQuery(dbContext);

        var touching = await query.GetAsync(
            new GetCrewAvailabilityConflictsRequest { CrewMemberId = member.Id, From = new DateOnly(2026, 8, 10), To = new DateOnly(2026, 8, 20) },
            CancellationToken.None);
        var clear = await query.GetAsync(
            new GetCrewAvailabilityConflictsRequest { CrewMemberId = member.Id, From = new DateOnly(2026, 8, 10), To = new DateOnly(2026, 8, 19) },
            CancellationToken.None);

        Assert.Single(touching.Conflicts);
        Assert.Empty(clear.Conflicts);
    }

    [Fact]
    public async Task GetAsync_rejects_missing_parameters()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var query = new GetCrewAvailabilityConflictsQuery(dbContext);

        await Assert.ThrowsAsync<GetCrewAvailabilityConflictsValidationException>(
            () => query.GetAsync(new GetCrewAvailabilityConflictsRequest { CrewMemberId = Guid.NewGuid() }, CancellationToken.None));
    }
}
