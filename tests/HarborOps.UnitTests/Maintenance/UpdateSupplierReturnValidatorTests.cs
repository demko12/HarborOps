using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class UpdateSupplierReturnValidatorTests
{
    [Fact]
    public void Validate_accepts_a_token_and_lines()
    {
        var errors = UpdateSupplierReturnValidator.Validate(new UpdateSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Lines = [Line()],
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_requires_the_concurrency_token()
    {
        var errors = UpdateSupplierReturnValidator.Validate(new UpdateSupplierReturnRequest { Lines = [Line()] });

        Assert.Contains(UpdateSupplierReturnValidator.ExpectedConcurrencyTokenField, errors.Keys);
    }

    [Fact]
    public void Validate_requires_at_least_one_line()
    {
        var errors = UpdateSupplierReturnValidator.Validate(new UpdateSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Lines = [],
        });

        Assert.Contains(SupplierReturnLinesValidator.LinesField, errors.Keys);
    }

    [Fact]
    public void Validate_checks_every_line()
    {
        var errors = UpdateSupplierReturnValidator.Validate(new UpdateSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Lines = [Line(), Line() with { Quantity = -2m, Reason = null }],
        });

        Assert.Contains("lines[1].quantity", errors.Keys);
        Assert.Contains("lines[1].reason", errors.Keys);
    }

    private static SupplierReturnLineEntry Line()
    {
        return new SupplierReturnLineEntry
        {
            PurchaseOrderLineId = Guid.NewGuid(),
            Quantity = 1m,
            Reason = "Damaged",
        };
    }
}
