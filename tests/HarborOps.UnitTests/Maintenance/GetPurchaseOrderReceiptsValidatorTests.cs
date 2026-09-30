using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPurchaseOrderReceiptsValidatorTests
{
    [Fact]
    public void Validate_accepts_defaults_and_the_largest_page()
    {
        Assert.Empty(GetPurchaseOrderReceiptsValidator.Validate(new GetPurchaseOrderReceiptsRequest()));
        Assert.Empty(GetPurchaseOrderReceiptsValidator.Validate(new GetPurchaseOrderReceiptsRequest
        {
            PageNumber = 1,
            PageSize = GetPurchaseOrderReceiptsValidator.MaxPageSize,
        }));
    }

    [Theory]
    [InlineData(0, 25, GetPurchaseOrderReceiptsValidator.PageNumberField)]
    [InlineData(1, 0, GetPurchaseOrderReceiptsValidator.PageSizeField)]
    [InlineData(1, 101, GetPurchaseOrderReceiptsValidator.PageSizeField)]
    public void Validate_rejects_invalid_paging(int pageNumber, int pageSize, string field)
    {
        var errors = GetPurchaseOrderReceiptsValidator.Validate(new GetPurchaseOrderReceiptsRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
        });

        Assert.Equal([field], errors.Keys);
    }
}
