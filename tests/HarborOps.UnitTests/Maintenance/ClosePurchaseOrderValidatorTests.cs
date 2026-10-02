using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class ClosePurchaseOrderValidatorTests
{
    [Fact]
    public void Validate_accepts_a_complete_request()
    {
        Assert.Empty(ClosePurchaseOrderValidator.Validate(ValidRequest()));
    }

    [Fact]
    public void Validate_requires_the_client_request_id_both_tokens_and_a_reason()
    {
        var errors = ClosePurchaseOrderValidator.Validate(new ClosePurchaseOrderRequest());

        Assert.Equal(
            [
                ClosePurchaseOrderValidator.ClientRequestIdField,
                ClosePurchaseOrderValidator.ExpectedConcurrencyTokenField,
                ClosePurchaseOrderValidator.PreviewTokenField,
                ClosePurchaseOrderValidator.ReasonField,
            ],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Validate_rejects_empty_identifiers_and_a_blank_preview_token()
    {
        var errors = ClosePurchaseOrderValidator.Validate(
            ValidRequest() with { ClientRequestId = Guid.Empty, ExpectedConcurrencyToken = Guid.Empty, PreviewToken = " " });

        Assert.Equal(
            [
                ClosePurchaseOrderValidator.ClientRequestIdField,
                ClosePurchaseOrderValidator.ExpectedConcurrencyTokenField,
                ClosePurchaseOrderValidator.PreviewTokenField,
            ],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Validate_rejects_an_oversized_preview_token()
    {
        var errors = ClosePurchaseOrderValidator.Validate(
            ValidRequest() with { PreviewToken = new string('A', ClosePurchaseOrderValidator.PreviewTokenMaxLength + 1) });

        Assert.Equal([ClosePurchaseOrderValidator.PreviewTokenField], errors.Keys);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("   too short   ")]
    public void Validate_rejects_a_reason_that_is_not_substantive(string reason)
    {
        var errors = ClosePurchaseOrderValidator.Validate(ValidRequest() with { Reason = reason });

        Assert.Equal([ClosePurchaseOrderValidator.ReasonField], errors.Keys);
    }

    [Fact]
    public void Validate_accepts_reasons_at_both_length_bounds()
    {
        Assert.Empty(ClosePurchaseOrderValidator.Validate(
            ValidRequest() with { Reason = new string('R', PurchaseOrderClosure.ReasonMinLength) }));
        Assert.Empty(ClosePurchaseOrderValidator.Validate(
            ValidRequest() with { Reason = new string('R', PurchaseOrderClosure.ReasonMaxLength) }));
    }

    [Fact]
    public void Validate_rejects_an_oversized_reason()
    {
        var errors = ClosePurchaseOrderValidator.Validate(
            ValidRequest() with { Reason = new string('R', PurchaseOrderClosure.ReasonMaxLength + 1) });

        Assert.Equal([ClosePurchaseOrderValidator.ReasonField], errors.Keys);
    }

    private static ClosePurchaseOrderRequest ValidRequest()
    {
        return new ClosePurchaseOrderRequest
        {
            ClientRequestId = Guid.NewGuid(),
            ExpectedConcurrencyToken = Guid.NewGuid(),
            PreviewToken = "ABC123",
            Reason = "Supplier discontinued the part",
        };
    }
}
