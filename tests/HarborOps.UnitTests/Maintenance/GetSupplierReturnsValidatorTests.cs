using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetSupplierReturnsValidatorTests
{
    [Fact]
    public void Validate_accepts_defaults_and_known_status_in_any_case()
    {
        Assert.Empty(GetSupplierReturnsValidator.Validate(new GetSupplierReturnsRequest()));
        Assert.Empty(GetSupplierReturnsValidator.Validate(new GetSupplierReturnsRequest { Status = "posted" }));
    }

    [Theory]
    [InlineData("Archived")]
    [InlineData("7")]
    public void Validate_rejects_an_unknown_status(string status)
    {
        var errors = GetSupplierReturnsValidator.Validate(new GetSupplierReturnsRequest { Status = status });

        Assert.Contains(GetSupplierReturnsValidator.StatusField, errors.Keys);
    }

    [Fact]
    public void Validate_rejects_empty_filter_ids()
    {
        var errors = GetSupplierReturnsValidator.Validate(new GetSupplierReturnsRequest
        {
            PurchaseOrderId = Guid.Empty,
            SupplierId = Guid.Empty,
        });

        Assert.Contains(GetSupplierReturnsValidator.PurchaseOrderIdField, errors.Keys);
        Assert.Contains(GetSupplierReturnsValidator.SupplierIdField, errors.Keys);
    }

    [Theory]
    [InlineData(0, 25, GetSupplierReturnsValidator.PageNumberField)]
    [InlineData(1, 0, GetSupplierReturnsValidator.PageSizeField)]
    [InlineData(1, 101, GetSupplierReturnsValidator.PageSizeField)]
    public void Validate_bounds_paging(int pageNumber, int pageSize, string field)
    {
        var errors = GetSupplierReturnsValidator.Validate(new GetSupplierReturnsRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
        });

        Assert.Contains(field, errors.Keys);
    }
}
