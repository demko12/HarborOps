using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Components.Pages.Compliance;

namespace HarborOps.Web.Tests.Components;

public sealed class PscInspectionDetailsPageTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    [Theory]
    [InlineData("2026-09-28", "Open", "2 days overdue", "overdue")]
    [InlineData("2026-09-29", "InProgress", "1 day overdue", "overdue")]
    [InlineData("2026-09-30", "Open", "due today", "open")]
    [InlineData("2026-10-01", "Open", "due in 1 day", "open")]
    [InlineData("2026-10-02", "InProgress", "due in 2 days", "open")]
    [InlineData("2026-09-20", "Closed", "", "closed")]
    public void Deficiency_deadlines_are_described_relative_to_today(string deadline, string status, string label, string state)
    {
        var item = Deficiency(DateOnly.Parse(deadline, System.Globalization.CultureInfo.InvariantCulture), status);

        Assert.Equal(label, PscInspectionDetails.DueLabel(item, Today));
        Assert.Equal(state, PscInspectionDetails.DeficiencyStateClass(item, Today));
    }

    [Fact]
    public void A_detained_inspection_with_open_findings_is_blocked_at_the_closing_step()
    {
        var steps = PscInspectionDetails.ReleaseSteps(Inspection("Detained", reported: 5, registered: 5, open: 2, closed: 3), overdueCount: 1);

        Assert.Equal(
            [PscInspectionDetails.StepDone, PscInspectionDetails.StepDone, PscInspectionDetails.StepCurrent, PscInspectionDetails.StepPending],
            steps.Select(step => step.State));
        Assert.Equal("3 of 5 closed · 1 overdue", steps[2].Note);
        Assert.Equal("Blocked until all findings are closed", steps[3].Note);
    }

    [Fact]
    public void A_count_mismatch_keeps_the_registration_step_current()
    {
        var steps = PscInspectionDetails.ReleaseSteps(Inspection("Detained", reported: 5, registered: 4, open: 0, closed: 4, mismatch: true), overdueCount: 0);

        Assert.Equal(PscInspectionDetails.StepCurrent, steps[1].State);
        Assert.Equal(PscInspectionDetails.StepPending, steps[2].State);
    }

    [Fact]
    public void A_detained_inspection_reports_how_long_the_vessel_has_been_detained()
    {
        var inspection = Inspection("Detained", reported: 1, registered: 1, open: 1, closed: 0, detainedAt: new DateTimeOffset(2026, 9, 23, 10, 20, 0, TimeSpan.Zero));

        Assert.Equal("7 days", PscInspectionDetails.DetentionSummary(inspection, Today));
        Assert.Equal("Detained since 23 Sep 2026", PscInspectionDetails.DetentionNote(inspection));
    }

    [Fact]
    public void The_deadline_axis_covers_the_inspection_every_deadline_and_today()
    {
        var axis = PscInspectionDetails.DeadlineAxis(
            new DateOnly(2026, 9, 22),
            [Deficiency(new DateOnly(2026, 9, 25), "Closed"), Deficiency(new DateOnly(2026, 10, 2), "Open")],
            Today);

        Assert.Equal(new DateOnly(2026, 9, 22), axis.Start);
        Assert.Equal(11, axis.DayCount);
        Assert.NotNull(axis.TodayPercent);
        Assert.InRange(axis.Percent(new DateOnly(2026, 10, 2)), 90, 100);
    }

    private static PscDeficiencyItem Deficiency(DateOnly deadline, string status)
    {
        return new PscDeficiencyItem(1, "04110", "Emergency fire pump", "Critical", deadline, status, Guid.Empty, 0);
    }

    private static GetPscInspectionDetailResponse Inspection(
        string state,
        int reported,
        int registered,
        int open,
        int closed,
        bool mismatch = false,
        DateTimeOffset? detainedAt = null)
    {
        var history = detainedAt is { } at
            ? new[] { new PscDetentionDecisionItem(Guid.NewGuid(), "Detained", "HR-RJK-118", "Fire safety", null, null, Guid.NewGuid(), at) }
            : [];

        return new GetPscInspectionDetailResponse(
            118,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 22),
            "Harbour Master's Office Rijeka",
            reported,
            registered,
            open,
            closed,
            mismatch,
            state,
            history.FirstOrDefault(),
            history);
    }
}
