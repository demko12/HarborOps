using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PostPurchaseOrderReceiptValidatorTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Validate_accepts_a_complete_request()
    {
        Assert.Empty(PostPurchaseOrderReceiptValidator.Validate(ValidRequest(), UtcNow));
    }

    [Fact]
    public void Validate_requires_the_client_request_id_and_both_tokens()
    {
        var errors = PostPurchaseOrderReceiptValidator.Validate(
            ValidRequest() with { ClientRequestId = null, ExpectedConcurrencyToken = Guid.Empty, PreviewToken = " " },
            UtcNow);

        Assert.Contains(PostPurchaseOrderReceiptValidator.ClientRequestIdField, errors.Keys);
        Assert.Contains(PostPurchaseOrderReceiptValidator.ExpectedConcurrencyTokenField, errors.Keys);
        Assert.Contains(PostPurchaseOrderReceiptValidator.PreviewTokenField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_an_empty_client_request_id()
    {
        var errors = PostPurchaseOrderReceiptValidator.Validate(ValidRequest() with { ClientRequestId = Guid.Empty }, UtcNow);

        Assert.Equal([PostPurchaseOrderReceiptValidator.ClientRequestIdField], errors.Keys);
    }

    [Fact]
    public void Validate_rejects_an_oversized_preview_token()
    {
        var errors = PostPurchaseOrderReceiptValidator.Validate(
            ValidRequest() with { PreviewToken = new string('A', PostPurchaseOrderReceiptValidator.PreviewTokenMaxLength + 1) },
            UtcNow);

        Assert.Equal([PostPurchaseOrderReceiptValidator.PreviewTokenField], errors.Keys);
    }

    [Fact]
    public void Validate_checks_the_receipt_time_reference_notes_and_lines()
    {
        var errors = PostPurchaseOrderReceiptValidator.Validate(
            ValidRequest() with
            {
                ReceivedUtc = null,
                DeliveryReference = new string('R', 101),
                Notes = new string('N', 1001),
                Lines = [],
            },
            UtcNow);

        Assert.Equal(
            ["deliveryReference", "lines", "notes", "receivedUtc"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    private static PostPurchaseOrderReceiptRequest ValidRequest()
    {
        return new PostPurchaseOrderReceiptRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = Guid.NewGuid(),
            PreviewToken = "ABC123",
            ReceivedUtc = UtcNow.AddHours(-2),
            DeliveryReference = "DN-1",
            Notes = "Pallet 1 of 2",
            Lines = [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.NewGuid(), Quantity = 4m }],
        };
    }
}
