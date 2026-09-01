using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.UnitTests.Compliance;

public sealed class NonConformityDomainTests
{
    [Fact]
    public void Register_with_survey_id_sets_open_status_and_survey_source()
    {
        var nonConformity = NonConformity.Register(
            Guid.NewGuid(),
            NonConformity.SeverityMajor,
            new DateOnly(2026, 12, 1),
            surveyId: 5);

        Assert.Equal(NonConformity.ClosureStatusOpen, nonConformity.ClosureStatus);
        Assert.Equal(5, nonConformity.SurveyId);
        Assert.Null(nonConformity.PscId);
    }

    [Fact]
    public void Register_with_psc_id_sets_open_status_and_psc_source()
    {
        var nonConformity = NonConformity.Register(
            Guid.NewGuid(),
            NonConformity.SeverityCritical,
            new DateOnly(2026, 12, 1),
            pscId: 7);

        Assert.Equal(NonConformity.ClosureStatusOpen, nonConformity.ClosureStatus);
        Assert.Equal(7, nonConformity.PscId);
        Assert.Null(nonConformity.SurveyId);
    }

    [Fact]
    public void Register_maps_evidence_document_id_severity_and_deadline()
    {
        var evidenceDocumentId = Guid.NewGuid();
        var deadline = new DateOnly(2027, 3, 15);

        var nonConformity = NonConformity.Register(
            evidenceDocumentId,
            NonConformity.SeverityMinor,
            deadline,
            surveyId: 9);

        Assert.Equal(evidenceDocumentId, nonConformity.EvidenceDocumentId);
        Assert.Equal(NonConformity.SeverityMinor, nonConformity.Severity);
        Assert.Equal(deadline, nonConformity.CorrectiveActionDeadline);
    }

    [Fact]
    public void AddCorrectiveAction_on_open_finding_transitions_to_in_progress_and_adds_the_action()
    {
        var nonConformity = CreateNonConformity(NonConformity.ClosureStatusOpen);
        var actorUserId = Guid.NewGuid();
        var recordedAtUtc = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

        var action = nonConformity.AddCorrectiveAction("Inspected the valve.", actorUserId, recordedAtUtc, null);

        Assert.Equal(NonConformity.ClosureStatusInProgress, nonConformity.ClosureStatus);
        Assert.Same(action, Assert.Single(nonConformity.CorrectiveActions));
    }

    [Fact]
    public void AddCorrectiveAction_on_in_progress_finding_keeps_status_and_appends_the_action()
    {
        var nonConformity = CreateNonConformity(NonConformity.ClosureStatusInProgress);

        nonConformity.AddCorrectiveAction("First action.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        nonConformity.AddCorrectiveAction("Second action.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);

        Assert.Equal(NonConformity.ClosureStatusInProgress, nonConformity.ClosureStatus);
        Assert.Equal(2, nonConformity.CorrectiveActions.Count);
    }

    [Fact]
    public void AddCorrectiveAction_on_closed_finding_throws()
    {
        var nonConformity = CreateNonConformity(NonConformity.ClosureStatusClosed);

        Assert.Throws<InvalidNonConformityTransitionException>(
            () => nonConformity.AddCorrectiveAction("Too late.", Guid.NewGuid(), DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Close_on_in_progress_finding_with_a_prior_action_sets_all_closure_fields()
    {
        var nonConformity = CreateNonConformity(NonConformity.ClosureStatusInProgress);
        nonConformity.AddCorrectiveAction("Fixed it.", Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        var closedByUserId = Guid.NewGuid();
        var closedAtUtc = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var finalEvidenceDocumentId = Guid.NewGuid();

        nonConformity.Close("All corrective actions verified.", closedByUserId, closedAtUtc, finalEvidenceDocumentId);

        Assert.Equal(NonConformity.ClosureStatusClosed, nonConformity.ClosureStatus);
        Assert.Equal("All corrective actions verified.", nonConformity.ClosureNotes);
        Assert.Equal(closedByUserId, nonConformity.ClosedByUserId);
        Assert.Equal(closedAtUtc, nonConformity.ClosedAtUtc);
        Assert.Equal(finalEvidenceDocumentId, nonConformity.FinalEvidenceDocumentId);
    }

    [Fact]
    public void Close_on_open_finding_with_no_corrective_actions_throws()
    {
        var nonConformity = CreateNonConformity(NonConformity.ClosureStatusOpen);

        Assert.Throws<InvalidNonConformityTransitionException>(
            () => nonConformity.Close("Notes.", Guid.NewGuid(), DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Close_on_an_already_closed_finding_throws()
    {
        var nonConformity = CreateNonConformity(NonConformity.ClosureStatusClosed);

        Assert.Throws<InvalidNonConformityTransitionException>(
            () => nonConformity.Close("Notes again.", Guid.NewGuid(), DateTimeOffset.UtcNow, null));
    }

    private static NonConformity CreateNonConformity(string closureStatus) => new(
        Guid.NewGuid(),
        NonConformity.SeverityMajor,
        new DateOnly(2026, 12, 1),
        closureStatus,
        surveyId: 1);
}
