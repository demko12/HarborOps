using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ConvertDefectToWorkOrderValidatorTests
{
    [Fact]
    public void Validate_accepts_a_valid_request()
    {
        var request = new ConvertDefectToWorkOrderRequest { DueUtc = DateTime.UtcNow.AddDays(7) };

        var errors = ConvertDefectToWorkOrderValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_missing_due_utc()
    {
        var request = new ConvertDefectToWorkOrderRequest { DueUtc = null };

        var errors = ConvertDefectToWorkOrderValidator.Validate(request);

        Assert.Contains(ConvertDefectToWorkOrderValidator.DueUtcField, errors);
    }
}
