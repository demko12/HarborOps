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
        var orderLine = ReceivedLine(ordered: 10m, received: 10m);

        var errors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 6.5m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine },
            new Dictionary<Guid, decimal> { [orderLine.Id] = 3.5m });

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateAgainstOrder_rejects_more_than_the_cumulative_remaining_allowance()
    {
        var orderLine = ReceivedLine(ordered: 10m, received: 10m);

        var errors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 6.501m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine },
            new Dictionary<Guid, decimal> { [orderLine.Id] = 3.5m });

        var message = Assert.Single(errors["lines[0].quantity"]);

        Assert.Contains("6.5", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAgainstOrder_caps_returns_at_the_received_not_the_ordered_quantity()
    {
        var orderLine = ReceivedLine(ordered: 10m, received: 4m);
        var lines = new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine };
        var posted = new Dictionary<Guid, decimal> { [orderLine.Id] = 1m };

        var atCeiling = SupplierReturnLinesValidator.ValidateAgainstOrder([Entry(orderLine.Id, 3m)], lines, posted);
        var aboveCeiling = SupplierReturnLinesValidator.ValidateAgainstOrder([Entry(orderLine.Id, 3.001m)], lines, posted);

        Assert.Empty(atCeiling);
        Assert.Contains("3", Assert.Single(aboveCeiling["lines[0].quantity"]), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAgainstOrder_rejects_any_return_from_an_unreceived_line()
    {
        var orderLine = PurchaseOrderLine.Create(PurchaseOrderId, Guid.NewGuid(), 10m, 5m);

        var errors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 0.001m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine },
            new Dictionary<Guid, decimal>());

        Assert.Equal(["lines[0].quantity"], errors.Keys);
    }

    [Fact]
    public void ValidateAgainstOrder_rejects_a_line_that_is_not_on_the_purchase_order()
    {
        var orderLine = ReceivedLine(ordered: 10m, received: 10m);

        var errors = SupplierReturnLinesValidator.ValidateAgainstOrder(
            [Entry(orderLine.Id, 1m), Entry(Guid.NewGuid(), 1m)],
            new Dictionary<Guid, PurchaseOrderLine> { [orderLine.Id] = orderLine },
            new Dictionary<Guid, decimal>());

        Assert.Equal(["lines[1].purchaseOrderLineId"], errors.Keys);
    }

    private static PurchaseOrderLine ReceivedLine(decimal ordered, decimal received)
    {
        var line = PurchaseOrderLine.Create(PurchaseOrderId, Guid.NewGuid(), ordered, 5m);
        line.Receive(received);

        return line;
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
