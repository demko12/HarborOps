using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RejectDefectValidatorTests
{
    [Fact]
    public void Validate_accepts_a_valid_request()
    {
        var errors = RejectDefectValidator.Validate(new RejectDefectRequest { Reason = "Duplicate report" });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_a_null_reason()
    {
        var errors = RejectDefectValidator.Validate(new RejectDefectRequest { Reason = null });

        Assert.Contains(RejectDefectValidator.ReasonField, errors);
    }

    [Fact]
    public void Validate_rejects_an_empty_reason()
    {
        var errors = RejectDefectValidator.Validate(new RejectDefectRequest { Reason = string.Empty });

        Assert.Contains(RejectDefectValidator.ReasonField, errors);
    }

    [Fact]
    public void Validate_rejects_a_whitespace_reason()
    {
        var errors = RejectDefectValidator.Validate(new RejectDefectRequest { Reason = "   " });

        Assert.Contains(RejectDefectValidator.ReasonField, errors);
    }
}
