using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PreviewPurchaseOrderReceiptValidatorTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Validate_accepts_a_receipt_time_and_lines()
    {
        var errors = PreviewPurchaseOrderReceiptValidator.Validate(
            new PreviewPurchaseOrderReceiptRequest
            {
                ReceivedUtc = UtcNow,
                Lines = [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.NewGuid(), Quantity = 1m }],
            },
            UtcNow);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_requires_a_receipt_time_and_lines()
    {
        var errors = PreviewPurchaseOrderReceiptValidator.Validate(new PreviewPurchaseOrderReceiptRequest(), UtcNow);

        Assert.Equal(["lines", "receivedUtc"], errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Validate_rejects_a_future_receipt_time()
    {
        var errors = PreviewPurchaseOrderReceiptValidator.Validate(
            new PreviewPurchaseOrderReceiptRequest
            {
                ReceivedUtc = UtcNow.AddMinutes(1),
                Lines = [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.NewGuid(), Quantity = 1m }],
            },
            UtcNow);

        Assert.Equal([PurchaseOrderReceiptLinesValidator.ReceivedUtcField], errors.Keys);
    }
}
