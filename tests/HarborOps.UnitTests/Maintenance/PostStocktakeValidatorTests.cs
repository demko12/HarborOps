using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PostStocktakeValidatorTests
{
    [Fact]
    public void Validate_accepts_a_token_and_preview_token()
    {
        var errors = PostStocktakeValidator.Validate(new PostStocktakeRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            PreviewToken = "ABC123",
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_requires_the_concurrency_token_and_preview_token()
    {
        var errors = PostStocktakeValidator.Validate(new PostStocktakeRequest());

        Assert.Contains(PostStocktakeValidator.ExpectedConcurrencyTokenField, errors.Keys);
        Assert.Contains(PostStocktakeValidator.PreviewTokenField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_an_oversized_preview_token()
    {
        var errors = PostStocktakeValidator.Validate(new PostStocktakeRequest
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            PreviewToken = new string('A', 129),
        });

        Assert.Contains(PostStocktakeValidator.PreviewTokenField, errors.Keys);
    }
}
