using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class SupplierReturnLinesValidatorTests
{
    private static readonly Guid PurchaseOrderId = Guid.NewGuid();

    [Fact]
    public void ValidateAgainstOrder_accepts_the_exact_remaining_quantity()
    {
        var orderLine = PurchaseOrderLine.Create(PurchaseOrderId, Guid.NewGuid(), 10m, 5m);

        var errors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 6.5m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine },
            new Dictionary<Guid, decimal> { [orderLine.Id] = 3.5m });

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateAgainstOrder_rejects_more_than_the_cumulative_remaining_allowance()
    {
        var orderLine = PurchaseOrderLine.Create(PurchaseOrderId, Guid.NewGuid(), 10m, 5m);

        var errors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 6.501m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine },
            new Dictionary<Guid, decimal> { [orderLine.Id] = 3.5m });

        var message = Assert.Single(errors["lines[0].quantity"]);

        Assert.Contains("6.5", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAgainstOrder_rejects_a_line_that_is_not_on_the_purchase_order()
    {
        var orderLine = PurchaseOrderLine.Create(PurchaseOrderId, Guid.NewGuid(), 10m, 5m);

        var errors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 1m), Entry(Guid.NewGuid(), 1m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine },
            new Dictionary<Guid, decimal>());

        Assert.Equal(["lines[1].purchaseOrderLineId"], errors.Keys);
    }

    private static SupplierReturnLineEntry Entry(Guid lineId, decimal quantity)
    {
        return new SupplierReturnLineEntry
        {
            PurchaseOrderLineId = lineId,
            Quantity = quantity,
            Reason = "Damaged",
        };
    }
}
