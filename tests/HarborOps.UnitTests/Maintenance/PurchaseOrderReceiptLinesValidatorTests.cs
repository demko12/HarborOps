using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PurchaseOrderReceiptLinesValidatorTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidateReceivedUtc_accepts_now_and_the_past()
    {
        var errors = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(UtcNow, required: true, UtcNow, errors);
        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(UtcNow.AddDays(-30), required: true, UtcNow, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateReceivedUtc_rejects_a_future_time()
    {
        var errors = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(UtcNow.AddSeconds(1), required: true, UtcNow, errors);

        Assert.Equal(["Received UTC cannot be in the future."], errors[PurchaseOrderReceiptLinesValidator.ReceivedUtcField]);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ValidateReceivedUtc_rejects_a_time_that_is_not_utc(DateTimeKind kind)
    {
        var errors = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(
            DateTime.SpecifyKind(UtcNow.AddHours(-1), kind),
            required: true,
            UtcNow,
            errors);

        Assert.Equal(["Received UTC must be in UTC format."], errors[PurchaseOrderReceiptLinesValidator.ReceivedUtcField]);
    }

    [Fact]
    public void ValidateReceivedUtc_requires_a_value_only_when_asked_to()
    {
        var required = new Dictionary<string, string[]>();
        var optional = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(null, required: true, UtcNow, required);
        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(null, required: false, UtcNow, optional);

        Assert.Contains(PurchaseOrderReceiptLinesValidator.ReceivedUtcField, required.Keys);
        Assert.Empty(optional);
    }

    [Fact]
    public void ValidateDeliveryReference_and_notes_are_bounded_after_trimming()
    {
        var atLimit = new Dictionary<string, string[]>();
        var overLimit = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateDeliveryReference(
            $"  {new string('R', PurchaseOrderReceipt.DeliveryReferenceMaxLength)}  ",
            atLimit);
        PurchaseOrderReceiptLinesValidator.ValidateNotes($"  {new string('N', PurchaseOrderReceipt.NotesMaxLength)}  ", atLimit);
        PurchaseOrderReceiptLinesValidator.ValidateDeliveryReference(
            new string('R', PurchaseOrderReceipt.DeliveryReferenceMaxLength + 1),
            overLimit);
        PurchaseOrderReceiptLinesValidator.ValidateNotes(new string('N', PurchaseOrderReceipt.NotesMaxLength + 1), overLimit);

        Assert.Empty(atLimit);
        Assert.Contains(PurchaseOrderReceiptLinesValidator.DeliveryReferenceField, overLimit.Keys);
        Assert.Contains(PurchaseOrderReceiptLinesValidator.NotesField, overLimit.Keys);
    }

    [Fact]
    public void ValidateLines_rejects_an_empty_receipt()
    {
        var missing = new Dictionary<string, string[]>();
        var empty = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateLines(null, missing);
        PurchaseOrderReceiptLinesValidator.ValidateLines([], empty);

        Assert.Equal(["At least one receipt line is required."], missing[PurchaseOrderReceiptLinesValidator.LinesField]);
        Assert.Equal(["At least one receipt line is required."], empty[PurchaseOrderReceiptLinesValidator.LinesField]);
    }

    [Fact]
    public void ValidateLines_limits_the_number_of_lines()
    {
        var errors = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateLines(
            [.. Enumerable.Range(0, PurchaseOrderReceipt.MaxLines + 1).Select(_ => Entry(Guid.NewGuid(), 1m))],
            errors);

        Assert.Contains(PurchaseOrderReceiptLinesValidator.LinesField, errors.Keys);
    }

    [Fact]
    public void ValidateLines_rejects_duplicate_purchase_order_lines()
    {
        var lineId = Guid.NewGuid();
        var errors = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateLines([Entry(lineId, 1m), Entry(lineId, 2m)], errors);

        Assert.Equal(["lines[1].purchaseOrderLineId"], errors.Keys);
    }

    [Fact]
    public void ValidateLines_requires_a_line_id_and_quantity()
    {
        var errors = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateLines(
            [
                new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = null, Quantity = 1m },
                new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.Empty, Quantity = 1m },
                new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.NewGuid(), Quantity = null },
                null!,
            ],
            errors);

        Assert.Equal(
            ["lines[0].purchaseOrderLineId", "lines[1].purchaseOrderLineId", "lines[2].quantity", "lines[3]"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("0", "Received quantity must be greater than zero.")]
    [InlineData("-2", "Received quantity must be greater than zero.")]
    [InlineData("1000000000000000", "Received quantity must be greater than zero.")]
    [InlineData("1.0005", "Received quantity supports at most 3 decimal places.")]
    public void ValidateLines_rejects_invalid_quantities(string quantity, string expectedMessage)
    {
        var errors = new Dictionary<string, string[]>();

        PurchaseOrderReceiptLinesValidator.ValidateLines([Entry(Guid.NewGuid(), decimal.Parse(quantity))], errors);

        Assert.Equal([expectedMessage], errors["lines[0].quantity"]);
    }

    [Fact]
    public void ValidateAgainstOrder_flags_lines_that_are_not_on_the_order()
    {
        var orderLine = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 5m, 1m);

        var errors = PurchaseOrderReceiptLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 1m), Entry(Guid.NewGuid(), 1m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine });

        Assert.Equal(["lines[1].purchaseOrderLineId"], errors.Keys);
    }

    private static PurchaseOrderReceiptLineEntry Entry(Guid lineId, decimal quantity)
    {
        return new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = lineId, Quantity = quantity };
    }
}
