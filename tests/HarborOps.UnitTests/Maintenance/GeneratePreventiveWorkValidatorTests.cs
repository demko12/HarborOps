using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GeneratePreventiveWorkValidatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    [Fact]
    public void Validate_accepts_a_through_date_of_today()
    {
        var request = new GeneratePreventiveWorkRequest { Through = Today };

        Assert.Empty(GeneratePreventiveWorkValidator.Validate(request, Today));
    }

    [Fact]
    public void Validate_requires_a_through_date()
    {
        var errors = GeneratePreventiveWorkValidator.Validate(new GeneratePreventiveWorkRequest(), Today);

        Assert.Contains(GeneratePreventiveWorkValidator.ThroughField, errors);
    }

    [Fact]
    public void Validate_rejects_a_through_date_in_the_past()
    {
        var request = new GeneratePreventiveWorkRequest { Through = Today.AddDays(-1) };

        var errors = GeneratePreventiveWorkValidator.Validate(request, Today);

        Assert.Contains(GeneratePreventiveWorkValidator.ThroughField, errors);
    }

    [Fact]
    public void Validate_accepts_a_through_date_at_the_maximum_window()
    {
        var request = new GeneratePreventiveWorkRequest
        {
            Through = Today.AddDays(GeneratePreventiveWorkValidator.MaxWindowDays),
        };

        Assert.Empty(GeneratePreventiveWorkValidator.Validate(request, Today));
    }

    [Fact]
    public void Validate_rejects_an_unbounded_through_date()
    {
        var request = new GeneratePreventiveWorkRequest
        {
            Through = Today.AddDays(GeneratePreventiveWorkValidator.MaxWindowDays + 1),
        };

        var errors = GeneratePreventiveWorkValidator.Validate(request, Today);

        Assert.Contains(GeneratePreventiveWorkValidator.ThroughField, errors);
    }

    [Fact]
    public void Validate_rejects_an_unknown_priority()
    {
        var request = new GeneratePreventiveWorkRequest { Through = Today, Priority = "Urgent" };

        var errors = GeneratePreventiveWorkValidator.Validate(request, Today);

        Assert.Contains(GeneratePreventiveWorkValidator.PriorityField, errors);
    }

    [Fact]
    public void ValidateReplacement_accepts_a_request_without_a_through_date_or_priority()
    {
        Assert.Empty(GeneratePreventiveWorkValidator.ValidateReplacement(new GeneratePreventiveWorkRequest()));
    }

    [Fact]
    public void ValidateReplacement_accepts_a_known_priority()
    {
        var request = new GeneratePreventiveWorkRequest { Priority = "high" };

        Assert.Empty(GeneratePreventiveWorkValidator.ValidateReplacement(request));
    }

    [Fact]
    public void ValidateReplacement_rejects_an_unknown_priority()
    {
        var request = new GeneratePreventiveWorkRequest { Priority = "Urgent" };

        var errors = GeneratePreventiveWorkValidator.ValidateReplacement(request);

        Assert.Contains(GeneratePreventiveWorkValidator.PriorityField, errors);
        Assert.DoesNotContain(GeneratePreventiveWorkValidator.ThroughField, errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePriority_defaults_to_medium(string? priority)
    {
        Assert.Equal(WorkOrderPriority.Medium, GeneratePreventiveWorkValidator.ResolvePriority(priority));
    }

    [Fact]
    public void ResolvePriority_parses_a_requested_priority_case_insensitively()
    {
        Assert.Equal(WorkOrderPriority.Critical, GeneratePreventiveWorkValidator.ResolvePriority("critical"));
    }
}
