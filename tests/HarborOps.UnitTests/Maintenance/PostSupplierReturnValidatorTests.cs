using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PostSupplierReturnValidatorTests
{
    [Fact]
    public void Validate_accepts_a_token_and_preview_token()
    {
        var errors = PostSupplierReturnValidator.Validate(new PostSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            PreviewToken = "ABC123",
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_requires_both_tokens()
    {
        var errors = PostSupplierReturnValidator.Validate(new PostSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.Empty,
        });

        Assert.Contains(PostSupplierReturnValidator.ExpectedConcurrencyTokenField, errors.Keys);
        Assert.Contains(PostSupplierReturnValidator.PreviewTokenField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_an_oversized_preview_token()
    {
        var errors = PostSupplierReturnValidator.Validate(new PostSupplierReturnRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            PreviewToken = new string('A', PostSupplierReturnValidator.PreviewTokenMaxLength + 1),
        });

        Assert.Contains(PostSupplierReturnValidator.PreviewTokenField, errors.Keys);
    }
}
