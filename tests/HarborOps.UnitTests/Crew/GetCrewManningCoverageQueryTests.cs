using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewManningCoverageQueryTests
{
    [Fact]
    public async Task GetAsync_marks_dates_without_a_plan_as_not_configured_not_zero()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5)), CancellationToken.None);

        Assert.All(response.Days, day => Assert.Null(day.RequiredHeadcount));
        Assert.All(response.Days, day => Assert.Null(day.Shortage));
        Assert.Empty(response.ShortageIntervals);
    }

    [Fact]
    public async Task GetAsync_shows_a_rank_covered_only_by_an_assignment_with_no_plan_line()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var cook = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Cook");
        var vesselId = Guid.NewGuid();
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), [(bosun.Id, 1)]);
        var cookMember = await VesselManningPlanTestData.SeedMemberAsync(dbContext, cook.Id);
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, cookMember.Id, vesselId, cook.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)), CancellationToken.None);

        var cookDay = Assert.Single(response.Days, day => day.RankId == cook.Id);
        Assert.Null(cookDay.RequiredHeadcount);
        Assert.Equal(1, cookDay.ProjectedAssignedHeadcount);
    }

    [Theory]
    [InlineData(CrewAssignment.PlannedStatus, false, true)] // planned: contributes through the planned sign-off
    [InlineData(CrewAssignment.ActiveStatus, false, true)] // active: contributes through the planned sign-off
    [InlineData(CrewAssignment.CompletedStatus, true, false)] // completed: contributes only through the actual sign-off, which is earlier
    [InlineData(CrewAssignment.CancelledStatus, false, false)] // cancelled: never contributes
    public async Task GetAsync_applies_the_date_boundary_rule_per_assignment_status(
        string status, bool useEarlierActualSignOff, bool expectContributionOnPlannedSignOffDate)
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);
        var plannedSignOff = new DateOnly(2026, 1, 10);
        var actualSignOff = useEarlierActualSignOff ? new DateOnly(2026, 1, 6) : (DateOnly?)null;

        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), plannedSignOff, status, actualSignOff);

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, plannedSignOff, plannedSignOff), CancellationToken.None);

        var day = response.Days.SingleOrDefault(d => d.RankId == bosun.Id);

        if (expectContributionOnPlannedSignOffDate)
        {
            Assert.NotNull(day);
            Assert.Equal(1, day!.ProjectedAssignedHeadcount);
        }
        else
        {
            Assert.Null(day);
        }
    }

    [Fact]
    public async Task GetAsync_counts_a_crew_member_once_per_rank_per_day_despite_overlapping_assignments()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);

        // Relief handover: the outgoing assignment's planned sign-off and the incoming assignment's
        // sign-on both land on the same date for the SAME member (e.g. a corrected re-assignment).
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 20));

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10)), CancellationToken.None);

        var day = response.Days.Single(d => d.RankId == bosun.Id);
        Assert.Equal(1, day.ProjectedAssignedHeadcount);
    }

    [Fact]
    public async Task GetAsync_reflects_a_relief_handover_between_two_members_as_two_on_the_handover_day()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        var outgoing = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);
        var incoming = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);

        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, outgoing.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, incoming.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 20));

        var handoverDay = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10)), CancellationToken.None);
        var beforeHandover = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 9)), CancellationToken.None);

        Assert.Equal(2, handoverDay.Days.Single(d => d.RankId == bosun.Id).ProjectedAssignedHeadcount);
        Assert.Equal(1, beforeHandover.Days.Single(d => d.RankId == bosun.Id).ProjectedAssignedHeadcount);
    }

    [Fact]
    public async Task GetAsync_uses_the_assignment_rank_not_the_members_current_profile_rank()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var cook = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Cook");
        var vesselId = Guid.NewGuid();
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id); // base rank Bosun
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, cook.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5)); // assigned as Cook

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)), CancellationToken.None);

        Assert.Contains(response.Days, d => d.RankId == cook.Id && d.ProjectedAssignedHeadcount == 1);
        Assert.DoesNotContain(response.Days, d => d.RankId == bosun.Id);
    }

    [Theory]
    [InlineData(CrewEmploymentStatuses.OnLeave)]
    [InlineData(CrewEmploymentStatuses.Suspended)]
    [InlineData(CrewEmploymentStatuses.Terminated)]
    public async Task GetAsync_excludes_a_member_with_inactive_employment_and_reports_why(string employmentStatus)
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), [(bosun.Id, 1)]);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id, employmentStatus);
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)), CancellationToken.None);

        var day = response.Days.Single(d => d.RankId == bosun.Id);
        Assert.Equal(1, day.ProjectedAssignedHeadcount);
        Assert.Equal(0, day.EligibleHeadcount);
        Assert.Equal(1, day.Shortage);
        Assert.Contains(day.IneligibleMembers, m => m.CrewMemberId == member.Id && m.Reason == "InactiveEmployment");
    }

    [Theory]
    [InlineData(CrewAvailabilityRequest.ApprovedStatus, true)]
    [InlineData(CrewAvailabilityRequest.RequestedStatus, false)]
    [InlineData(CrewAvailabilityRequest.RejectedStatus, false)]
    [InlineData(CrewAvailabilityRequest.CancelledStatus, false)]
    public async Task GetAsync_excludes_only_approved_overlapping_unavailability(string status, bool expectExcluded)
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), [(bosun.Id, 1)]);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4), status);

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 3)), CancellationToken.None);

        var day = response.Days.Single(d => d.RankId == bosun.Id);

        if (expectExcluded)
        {
            Assert.Equal(0, day.EligibleHeadcount);
            Assert.Contains(day.IneligibleMembers, m => m.Reason == "ApprovedUnavailability");
        }
        else
        {
            Assert.Equal(1, day.EligibleHeadcount);
            Assert.Empty(day.IneligibleMembers);
        }
    }

    [Fact]
    public async Task GetAsync_does_not_expose_the_unavailability_reason_text()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), [(bosun.Id, 1)]);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4), CrewAvailabilityRequest.ApprovedStatus);

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 3)), CancellationToken.None);

        var reason = response.Days.Single(d => d.RankId == bosun.Id).IneligibleMembers.Single().Reason;
        Assert.DoesNotContain("Seeded", reason, StringComparison.Ordinal);
        Assert.Equal("ApprovedUnavailability", reason);
    }

    [Fact]
    public async Task GetAsync_collapses_consecutive_equal_shortage_dates_into_one_interval_per_rank()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        // Required headcount of 2, zero assigned, for the whole window -> a constant shortage of 2.
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10), [(bosun.Id, 2)]);

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5)), CancellationToken.None);

        var interval = Assert.Single(response.ShortageIntervals);
        Assert.Equal(bosun.Id, interval.RankId);
        Assert.Equal(new DateOnly(2026, 1, 1), interval.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 5), interval.EndDate);
        Assert.Equal(2, interval.Shortage);
    }

    [Fact]
    public async Task GetAsync_starts_a_new_interval_when_the_shortage_amount_changes()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5), [(bosun.Id, 2)]);
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 10), [(bosun.Id, 3)]);

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), CancellationToken.None);

        Assert.Equal(2, response.ShortageIntervals.Count);
        Assert.Contains(response.ShortageIntervals, i => i.StartDate == new DateOnly(2026, 1, 1) && i.EndDate == new DateOnly(2026, 1, 5) && i.Shortage == 2);
        Assert.Contains(response.ShortageIntervals, i => i.StartDate == new DateOnly(2026, 1, 6) && i.EndDate == new DateOnly(2026, 1, 10) && i.Shortage == 3);
    }

    [Fact]
    public async Task GetAsync_excludes_fully_staffed_dates_from_shortage_intervals()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();
        var bosun = await VesselManningPlanTestData.SeedPositionAsync(dbContext, "Bosun");
        var vesselId = Guid.NewGuid();
        await VesselManningPlanTestData.SeedPlanAsync(
            dbContext, vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5), [(bosun.Id, 1)]);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, bosun.Id);
        await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, bosun.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        var response = await Query(dbContext).GetAsync(
            Request(vesselId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5)), CancellationToken.None);

        Assert.Empty(response.ShortageIntervals);
        Assert.All(response.Days, day => Assert.Equal(0, day.Shortage));
    }

    [Fact]
    public async Task GetAsync_rejects_missing_required_parameters()
    {
        await using var dbContext = VesselManningPlanTestData.CreateDbContext();

        await Assert.ThrowsAsync<GetCrewManningCoverageValidationException>(() => Query(dbContext).GetAsync(
            new GetCrewManningCoverageRequest { VesselId = Guid.NewGuid() }, CancellationToken.None));
    }

    private static GetCrewManningCoverageQuery Query(HarborOps.Infrastructure.Persistence.HarborOpsDbContext dbContext) => new(dbContext);

    private static GetCrewManningCoverageRequest Request(Guid vesselId, DateOnly from, DateOnly to) => new()
    {
        VesselId = vesselId,
        From = from,
        To = to,
    };
}
