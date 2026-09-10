using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.UnitTests.Compliance;

public sealed class AuditPlanTests
{
    [Fact]
    public void Create_starts_in_the_scheduled_status()
    {
        var plan = CreatePlan();

        Assert.Equal(AuditPlan.ScheduledStatus, plan.Status);
    }

    [Fact]
    public void Reschedule_updates_dates_while_scheduled()
    {
        var plan = CreatePlan();
        var newStart = new DateOnly(2026, 9, 10);
        var newDue = new DateOnly(2026, 9, 20);

        plan.Reschedule(newStart, newDue);

        Assert.Equal(newStart, plan.PlannedStartDate);
        Assert.Equal(newDue, plan.DueDate);
    }

    [Fact]
    public void Reschedule_throws_once_the_plan_has_started()
    {
        var plan = CreatePlan();
        plan.Assign(Guid.NewGuid());
        plan.Start(DateTime.UtcNow);

        var exception = Assert.Throws<InvalidAuditPlanTransitionException>(
            () => plan.Reschedule(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20)));

        Assert.Equal(AuditPlan.InProgressStatus, exception.CurrentStatus);
        Assert.Equal(AuditPlan.RescheduleAction, exception.AttemptedAction);
    }

    [Fact]
    public void Assign_sets_the_inspector_while_scheduled_or_in_progress()
    {
        var plan = CreatePlan();
        var inspectorId = Guid.NewGuid();

        plan.Assign(inspectorId);

        Assert.Equal(inspectorId, plan.AssignedInspectorUserId);
    }

    [Fact]
    public void Assign_throws_once_the_plan_is_completed()
    {
        var plan = CompletedPlan();

        Assert.Throws<InvalidAuditPlanTransitionException>(() => plan.Assign(Guid.NewGuid()));
    }

    [Fact]
    public void Start_throws_without_an_assigned_inspector()
    {
        var plan = CreatePlan();

        var exception = Assert.Throws<InvalidAuditPlanTransitionException>(() => plan.Start(DateTime.UtcNow));

        Assert.Equal(AuditPlan.StartAction, exception.AttemptedAction);
    }

    [Fact]
    public void Start_moves_an_assigned_scheduled_plan_to_in_progress()
    {
        var plan = CreatePlan();
        plan.Assign(Guid.NewGuid());
        var startedAt = new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc);

        plan.Start(startedAt);

        Assert.Equal(AuditPlan.InProgressStatus, plan.Status);
        Assert.Equal(startedAt, plan.StartedAtUtc);
    }

    [Fact]
    public void Start_throws_when_the_plan_is_not_scheduled()
    {
        var plan = CreatePlan();
        plan.Assign(Guid.NewGuid());
        plan.Start(DateTime.UtcNow);

        Assert.Throws<InvalidAuditPlanTransitionException>(() => plan.Start(DateTime.UtcNow));
    }

    [Fact]
    public void Complete_links_the_survey_and_moves_to_completed()
    {
        var plan = CreatePlan();
        plan.Assign(Guid.NewGuid());
        plan.Start(DateTime.UtcNow);
        var survey = new SurveyAudit(
            plan.VesselId, new DateOnly(2026, 9, 10), plan.Type, SurveyAudit.ResultSatisfactory, "All clear.");
        var completedAt = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

        plan.Complete(survey, completedAt);

        Assert.Equal(AuditPlan.CompletedStatus, plan.Status);
        Assert.Equal(completedAt, plan.CompletedAtUtc);
        Assert.Same(survey, plan.Survey);
    }

    [Fact]
    public void Complete_throws_when_the_plan_is_not_in_progress()
    {
        var plan = CreatePlan();
        var survey = new SurveyAudit(
            plan.VesselId, new DateOnly(2026, 9, 10), plan.Type, SurveyAudit.ResultSatisfactory, "All clear.");

        var exception = Assert.Throws<InvalidAuditPlanTransitionException>(
            () => plan.Complete(survey, DateTime.UtcNow));

        Assert.Equal(AuditPlan.ScheduledStatus, exception.CurrentStatus);
    }

    [Fact]
    public void Complete_throws_for_an_already_completed_plan()
    {
        var plan = CompletedPlan();
        var secondSurvey = new SurveyAudit(
            plan.VesselId, new DateOnly(2026, 9, 11), plan.Type, SurveyAudit.ResultSatisfactory, "Second attempt.");

        Assert.Throws<InvalidAuditPlanTransitionException>(() => plan.Complete(secondSurvey, DateTime.UtcNow));
    }

    [Fact]
    public void Cancel_records_the_reason_and_timestamp()
    {
        var plan = CreatePlan();
        var cancelledAt = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);

        plan.Cancel("Vessel drydocked earlier than planned.", cancelledAt);

        Assert.Equal(AuditPlan.CancelledStatus, plan.Status);
        Assert.Equal(cancelledAt, plan.CancelledAtUtc);
        Assert.Equal("Vessel drydocked earlier than planned.", plan.CancellationReason);
    }

    [Fact]
    public void Cancel_throws_for_an_already_completed_plan()
    {
        var plan = CompletedPlan();

        Assert.Throws<InvalidAuditPlanTransitionException>(() => plan.Cancel("Too late.", DateTime.UtcNow));
    }

    [Fact]
    public void Cancel_throws_for_an_already_cancelled_plan()
    {
        var plan = CreatePlan();
        plan.Cancel("First reason.", DateTime.UtcNow);

        Assert.Throws<InvalidAuditPlanTransitionException>(() => plan.Cancel("Second reason.", DateTime.UtcNow));
    }

    private static AuditPlan CreatePlan()
    {
        return AuditPlan.Create(
            Guid.NewGuid(),
            SurveyAudit.TypeAnnualSurvey,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 15),
            assignedInspectorUserId: null,
            scope: "Full hull and machinery survey.",
            notes: null);
    }

    private static AuditPlan CompletedPlan()
    {
        var plan = CreatePlan();
        plan.Assign(Guid.NewGuid());
        plan.Start(DateTime.UtcNow);
        var survey = new SurveyAudit(
            plan.VesselId, new DateOnly(2026, 9, 10), plan.Type, SurveyAudit.ResultSatisfactory, "All clear.");
        plan.Complete(survey, DateTime.UtcNow);

        return plan;
    }
}
