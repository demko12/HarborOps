using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class TriageDefectValidatorTests
{
    [Fact]
    public void Validate_accepts_a_valid_request()
    {
        var errors = TriageDefectValidator.Validate(CreateValidRequest());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_a_request_without_notes()
    {
        var request = CreateValidRequest() with { Notes = null };

        var errors = TriageDefectValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_confirmed_severity()
    {
        var request = CreateValidRequest() with { ConfirmedSeverity = null };

        var errors = TriageDefectValidator.Validate(request);

        Assert.Contains(TriageDefectValidator.ConfirmedSeverityField, errors);
    }

    [Fact]
    public void Validate_rejects_an_invalid_confirmed_severity()
    {
        var request = CreateValidRequest() with { ConfirmedSeverity = "Severe" };

        var errors = TriageDefectValidator.Validate(request);

        Assert.Contains(TriageDefectValidator.ConfirmedSeverityField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_priority()
    {
        var request = CreateValidRequest() with { Priority = null };

        var errors = TriageDefectValidator.Validate(request);

        Assert.Contains(TriageDefectValidator.PriorityField, errors);
    }

    [Fact]
    public void Validate_rejects_an_invalid_priority()
    {
        var request = CreateValidRequest() with { Priority = "Urgent" };

        var errors = TriageDefectValidator.Validate(request);

        Assert.Contains(TriageDefectValidator.PriorityField, errors);
    }

    private static TriageDefectRequest CreateValidRequest()
    {
        return new TriageDefectRequest
        {
            ConfirmedSeverity = "High",
            Priority = "High",
            Notes = "Confirmed active leak, needs urgent repair",
        };
    }
}
