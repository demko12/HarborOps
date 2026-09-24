using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CancelSupplierReturnValidatorTests
{
    [Fact]
    public void Validate_accepts_a_token_and_reason()
    {
        var errors = CancelSupplierReturnValidator.Validate(new CancelSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Reason = "Supplier accepted a credit instead",
        });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Validate_requires_a_reason(string? reason)
    {
        var errors = CancelSupplierReturnValidator.Validate(new CancelSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Reason = reason,
        });

        Assert.Contains(CancelSupplierReturnValidator.ReasonField, errors.Keys);
    }

    [Fact]
    public void Validate_limits_the_reason_length()
    {
        var errors = CancelSupplierReturnValidator.Validate(new CancelSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Reason = new string('x', SupplierReturn.ReasonMaxLength + 1),
        });

        Assert.Contains(CancelSupplierReturnValidator.ReasonField, errors.Keys);
    }

    [Fact]
    public void Validate_requires_the_concurrency_token()
    {
        var errors = CancelSupplierReturnValidator.Validate(new CancelSupplierReturnRequest { Reason = "Duplicate" });

        Assert.Contains(CancelSupplierReturnValidator.ExpectedConcurrencyTokenField, errors.Keys);
    }
}
