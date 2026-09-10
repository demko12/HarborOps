using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RegisterDefectValidatorTests
{
    [Fact]
    public void Validate_accepts_a_valid_request()
    {
        var errors = RegisterDefectValidator.Validate(CreateValidRequest());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_a_request_without_equipment_or_description()
    {
        var request = CreateValidRequest() with { EquipmentId = null, Description = null };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_null_vessel_id()
    {
        var request = CreateValidRequest() with { VesselId = null };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.VesselIdField, errors);
    }

    [Fact]
    public void Validate_rejects_an_empty_vessel_id()
    {
        var request = CreateValidRequest() with { VesselId = Guid.Empty };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.VesselIdField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_title()
    {
        var request = CreateValidRequest() with { Title = null };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.TitleField, errors);
    }

    [Fact]
    public void Validate_rejects_a_whitespace_title()
    {
        var request = CreateValidRequest() with { Title = "   " };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.TitleField, errors);
    }

    [Fact]
    public void Validate_rejects_a_title_exceeding_the_max_length()
    {
        var request = CreateValidRequest() with { Title = new string('a', VesselDefect.TitleMaxLength + 1) };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.TitleField, errors);
    }

    [Fact]
    public void Validate_accepts_a_title_at_exactly_the_max_length()
    {
        var request = CreateValidRequest() with { Title = new string('a', VesselDefect.TitleMaxLength) };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.DoesNotContain(RegisterDefectValidator.TitleField, errors);
    }

    [Fact]
    public void Validate_rejects_a_description_exceeding_the_max_length()
    {
        var request = CreateValidRequest() with
        {
            Description = new string('a', VesselDefect.DescriptionMaxLength + 1),
        };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.DescriptionField, errors);
    }

    [Fact]
    public void Validate_accepts_a_description_at_exactly_the_max_length()
    {
        var request = CreateValidRequest() with
        {
            Description = new string('a', VesselDefect.DescriptionMaxLength),
        };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.DoesNotContain(RegisterDefectValidator.DescriptionField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_reported_severity()
    {
        var request = CreateValidRequest() with { ReportedSeverity = null };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.ReportedSeverityField, errors);
    }

    [Fact]
    public void Validate_rejects_an_invalid_reported_severity()
    {
        var request = CreateValidRequest() with { ReportedSeverity = "Urgent" };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.ReportedSeverityField, errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_observed_at_utc()
    {
        var request = CreateValidRequest() with { ObservedAtUtc = null };

        var errors = RegisterDefectValidator.Validate(request);

        Assert.Contains(RegisterDefectValidator.ObservedAtUtcField, errors);
    }

    private static RegisterDefectRequest CreateValidRequest()
    {
        return new RegisterDefectRequest
        {
            VesselId = Guid.NewGuid(),
            EquipmentId = Guid.NewGuid(),
            Title = "Hydraulic leak on crane boom",
            Description = "Visible oil leak near the boom cylinder",
            ReportedSeverity = "Medium",
            ObservedAtUtc = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
        };
    }
}
