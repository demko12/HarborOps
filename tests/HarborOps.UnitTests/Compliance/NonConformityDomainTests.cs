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
}
