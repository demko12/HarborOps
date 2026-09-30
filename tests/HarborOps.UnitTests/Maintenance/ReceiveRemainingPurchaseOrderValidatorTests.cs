using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ReceiveRemainingPurchaseOrderValidatorTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Validate_accepts_an_empty_request()
    {
        Assert.Empty(ReceiveRemainingPurchaseOrderValidator.Validate(new ReceiveRemainingPurchaseOrderRequest(), UtcNow));
    }

    [Fact]
    public void Validate_accepts_every_optional_field()
    {
        var errors = ReceiveRemainingPurchaseOrderValidator.Validate(
            new ReceiveRemainingPurchaseOrderRequest
            {
                ClientRequestId = Guid.NewGuid(),
                ExpectedConcurrencyToken = Guid.NewGuid(),
                PreviewToken = "ABC123",
                ReceivedUtc = UtcNow,
                DeliveryReference = "DN-9",
                Notes = "Final pallet",
            },
            UtcNow);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_checks_the_optional_fields_when_given()
    {
        var errors = ReceiveRemainingPurchaseOrderValidator.Validate(
            new ReceiveRemainingPurchaseOrderRequest
            {
                ClientRequestId = Guid.Empty,
                ExpectedConcurrencyToken = Guid.Empty,
                PreviewToken = new string('A', PostPurchaseOrderReceiptValidator.PreviewTokenMaxLength + 1),
                ReceivedUtc = UtcNow.AddMinutes(1),
                DeliveryReference = new string('R', 101),
                Notes = new string('N', 1001),
            },
            UtcNow);

        Assert.Equal(
            ["clientRequestId", "deliveryReference", "expectedConcurrencyToken", "notes", "previewToken", "receivedUtc"],
            errors.Keys.Order(StringComparer.Ordinal));
    }
}
