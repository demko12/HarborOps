using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.UnitTests.Compliance;

public sealed class RegisterNonConformityValidatorTests
{
    private static readonly FakeDateTimeProvider DateTimeProvider =
        new(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Validate_accepts_valid_request_with_only_survey_id()
    {
        var request = CreateValidRequest() with { SurveyId = 1, PscId = null };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_valid_request_with_only_psc_id()
    {
        var request = CreateValidRequest() with { SurveyId = null, PscId = 1 };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_neither_survey_id_nor_psc_id()
    {
        var request = CreateValidRequest() with { SurveyId = null, PscId = null };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.SourceField, errors);
    }

    [Fact]
    public void Validate_rejects_both_survey_id_and_psc_id()
    {
        var request = CreateValidRequest() with { SurveyId = 1, PscId = 2 };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.SourceField, errors);
    }

    [Fact]
    public void Validate_rejects_severity_outside_allowed_set()
    {
        var request = CreateValidRequest() with { Severity = "Severe" };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.SeverityField, errors);
    }

    [Fact]
    public void Validate_rejects_whitespace_severity()
    {
        var request = CreateValidRequest() with { Severity = "   " };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.SeverityField, errors);
    }

    [Fact]
    public void Validate_rejects_empty_evidence_document_id()
    {
        var request = CreateValidRequest() with { EvidenceDocumentId = Guid.Empty };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.EvidenceDocumentIdField, errors);
    }

    [Fact]
    public void Validate_rejects_null_evidence_document_id()
    {
        var request = CreateValidRequest() with { EvidenceDocumentId = null };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.EvidenceDocumentIdField, errors);
    }

    [Fact]
    public void Validate_rejects_deadline_equal_to_today()
    {
        var today = DateOnly.FromDateTime(DateTimeProvider.UtcNow);
        var request = CreateValidRequest() with { CorrectiveActionDeadline = today };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.CorrectiveActionDeadlineField, errors);
    }

    [Fact]
    public void Validate_rejects_deadline_in_the_past()
    {
        var today = DateOnly.FromDateTime(DateTimeProvider.UtcNow);
        var request = CreateValidRequest() with { CorrectiveActionDeadline = today.AddDays(-1) };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.CorrectiveActionDeadlineField, errors);
    }

    [Fact]
    public void Validate_rejects_null_deadline()
    {
        var request = CreateValidRequest() with { CorrectiveActionDeadline = null };

        var errors = RegisterNonConformityValidator.Validate(request, DateTimeProvider);

        Assert.Contains(RegisterNonConformityValidator.CorrectiveActionDeadlineField, errors);
    }

    private static RegisterNonConformityRequest CreateValidRequest()
    {
        var today = DateOnly.FromDateTime(DateTimeProvider.UtcNow);

        return new RegisterNonConformityRequest
        {
            SurveyId = 1,
            PscId = null,
            EvidenceDocumentId = Guid.NewGuid(),
            Severity = NonConformity.SeverityMinor,
            CorrectiveActionDeadline = today.AddDays(1),
        };
    }
}
