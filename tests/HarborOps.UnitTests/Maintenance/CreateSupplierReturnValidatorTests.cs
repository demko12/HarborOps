using System.Globalization;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CreateSupplierReturnValidatorTests
{
    [Fact]
    public void Validate_accepts_a_purchase_order_and_valid_lines()
    {
        var errors = CreateSupplierReturnValidator.Validate(CreateRequest(
            Line(Guid.NewGuid(), 1.125m),
            Line(Guid.NewGuid(), 2m)));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_requires_the_purchase_order()
    {
        var errors = CreateSupplierReturnValidator.Validate(CreateRequest(Line(Guid.NewGuid(), 1m)) with
        {
            PurchaseOrderId = Guid.Empty,
        });

        Assert.Contains(CreateSupplierReturnValidator.PurchaseOrderIdField, errors.Keys);
    }

    [Fact]
    public void Validate_requires_at_least_one_line()
    {
        var errors = CreateSupplierReturnValidator.Validate(CreateRequest());

        Assert.Contains(SupplierReturnLinesValidator.LinesField, errors.Keys);
    }

    [Fact]
    public void Validate_allows_at_most_one_hundred_lines()
    {
        var atLimit = CreateSupplierReturnValidator.Validate(CreateRequest(
            Enumerable.Range(0, SupplierReturn.MaxLines).Select(_ => Line(Guid.NewGuid(), 1m)).ToArray()));
        var overLimit = CreateSupplierReturnValidator.Validate(CreateRequest(
            Enumerable.Range(0, SupplierReturn.MaxLines + 1).Select(_ => Line(Guid.NewGuid(), 1m)).ToArray()));

        Assert.Empty(atLimit);
        Assert.Contains(SupplierReturnLinesValidator.LinesField, overLimit.Keys);
    }

    [Fact]
    public void Validate_rejects_duplicate_purchase_order_lines()
    {
        var lineId = Guid.NewGuid();

        var errors = CreateSupplierReturnValidator.Validate(CreateRequest(Line(lineId, 1m), Line(lineId, 2m)));

        Assert.Contains("lines[1].purchaseOrderLineId", errors.Keys);
        Assert.DoesNotContain("lines[0].purchaseOrderLineId", errors.Keys);
    }

    [Fact]
    public void Validate_requires_a_purchase_order_line_id()
    {
        var errors = CreateSupplierReturnValidator.Validate(CreateRequest(new SupplierReturnLineEntry
        {
            Quantity = 1m,
            Reason = "Damaged",
        }));

        Assert.Contains("lines[0].purchaseOrderLineId", errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0005")]
    [InlineData("1.0001")]
    public void Validate_rejects_missing_zero_negative_and_over_precise_quantities(string? quantity)
    {
        var errors = CreateSupplierReturnValidator.Validate(CreateRequest(Line(
            Guid.NewGuid(),
            quantity is null ? null : decimal.Parse(quantity, CultureInfo.InvariantCulture))));

        Assert.Contains("lines[0].quantity", errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Validate_requires_a_reason_on_every_line(string? reason)
    {
        var errors = CreateSupplierReturnValidator.Validate(CreateRequest(Line(Guid.NewGuid(), 1m, reason)));

        Assert.Contains("lines[0].reason", errors.Keys);
    }

    [Fact]
    public void Validate_limits_reason_and_notes_length()
    {
        var errors = CreateSupplierReturnValidator.Validate(
            CreateRequest(Line(Guid.NewGuid(), 1m, new string('x', SupplierReturn.ReasonMaxLength + 1))) with
            {
                Notes = new string('x', SupplierReturn.NotesMaxLength + 1),
            });

        Assert.Contains("lines[0].reason", errors.Keys);
        Assert.Contains(SupplierReturnLinesValidator.NotesField, errors.Keys);
    }

    private static CreateSupplierReturnRequest CreateRequest(params SupplierReturnLineEntry[] lines)
    {
        return new CreateSupplierReturnRequest
        {
            PurchaseOrderId = Guid.NewGuid(),
            Lines = lines,
        };
    }

    private static SupplierReturnLineEntry Line(Guid lineId, decimal? quantity, string? reason = "Damaged")
    {
        return new SupplierReturnLineEntry
        {
            PurchaseOrderLineId = lineId,
            Quantity = quantity,
            Reason = reason,
        };
    }
}
