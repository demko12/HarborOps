using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;


public sealed class PurchaseOrderDomainTests
{
    private static readonly DateOnly Today = new(2026, 8, 20);

    [Fact]
    public void Create_starts_the_order_in_draft()
    {
        var supplierId = Guid.NewGuid();

        var order = PurchaseOrder.Create(supplierId, Today, Today.AddDays(14));

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(supplierId, order.SupplierId);
        Assert.Equal(Today, order.OrderDate);
        Assert.Equal(Today.AddDays(14), order.ExpectedDelivery);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, PurchaseOrderStatus.Draft, false)]
    [InlineData(PurchaseOrderStatus.Draft, PurchaseOrderStatus.Ordered, true)]
    [InlineData(PurchaseOrderStatus.Draft, PurchaseOrderStatus.Received, false)]
    [InlineData(PurchaseOrderStatus.Draft, PurchaseOrderStatus.Cancelled, true)]
    [InlineData(PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Draft, false)]
    [InlineData(PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Ordered, false)]
    [InlineData(PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Received, true)]
    [InlineData(PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Cancelled, true)]
    [InlineData(PurchaseOrderStatus.Received, PurchaseOrderStatus.Draft, false)]
    [InlineData(PurchaseOrderStatus.Received, PurchaseOrderStatus.Ordered, false)]
    [InlineData(PurchaseOrderStatus.Received, PurchaseOrderStatus.Received, false)]
    [InlineData(PurchaseOrderStatus.Received, PurchaseOrderStatus.Cancelled, false)]
    [InlineData(PurchaseOrderStatus.Cancelled, PurchaseOrderStatus.Draft, false)]
    [InlineData(PurchaseOrderStatus.Cancelled, PurchaseOrderStatus.Ordered, false)]
    [InlineData(PurchaseOrderStatus.Cancelled, PurchaseOrderStatus.Received, false)]
    [InlineData(PurchaseOrderStatus.Cancelled, PurchaseOrderStatus.Cancelled, false)]
    public void CanTransition_follows_the_transition_matrix(
        PurchaseOrderStatus from,
        PurchaseOrderStatus to,
        bool expected)
    {
        Assert.Equal(expected, PurchaseOrder.CanTransition(from, to));
    }

    [Fact]
    public void AllowedTransitions_are_derived_per_status()
    {
        Assert.Equal(
            [PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Cancelled],
            PurchaseOrder.AllowedTransitions(PurchaseOrderStatus.Draft));

        Assert.Equal(
            [PurchaseOrderStatus.Received, PurchaseOrderStatus.Cancelled],
            PurchaseOrder.AllowedTransitions(PurchaseOrderStatus.Ordered));

        Assert.Empty(PurchaseOrder.AllowedTransitions(PurchaseOrderStatus.Received));
        Assert.Empty(PurchaseOrder.AllowedTransitions(PurchaseOrderStatus.Cancelled));
    }

    [Fact]
    public void ChangeStatusTo_walks_draft_to_ordered_to_received()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);

        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);

        order.ChangeStatusTo(PurchaseOrderStatus.Received);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
    }

    [Fact]
    public void ChangeStatusTo_rejects_skipping_the_ordered_state()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);

        var exception = Assert.Throws<PurchaseOrderTransitionException>(
            () => order.ChangeStatusTo(PurchaseOrderStatus.Received));

        Assert.Equal(order.Id, exception.PurchaseOrderId);
        Assert.Equal(PurchaseOrderStatus.Draft, exception.CurrentStatus);
        Assert.Equal(PurchaseOrderStatus.Received, exception.TargetStatus);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void ChangeStatusTo_rejects_a_second_receipt()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        order.ChangeStatusTo(PurchaseOrderStatus.Received);

        Assert.Throws<PurchaseOrderTransitionException>(
            () => order.ChangeStatusTo(PurchaseOrderStatus.Received));

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
    }

    [Fact]
    public void ChangeStatusTo_rejects_cancelling_a_received_order()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        order.ChangeStatusTo(PurchaseOrderStatus.Received);

        Assert.Throws<PurchaseOrderTransitionException>(
            () => order.ChangeStatusTo(PurchaseOrderStatus.Cancelled));

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, -1, true)]
    [InlineData(PurchaseOrderStatus.Ordered, -1, true)]
    [InlineData(PurchaseOrderStatus.Ordered, 0, false)]
    [InlineData(PurchaseOrderStatus.Ordered, 1, false)]
    [InlineData(PurchaseOrderStatus.Received, -1, false)]
    [InlineData(PurchaseOrderStatus.Cancelled, -1, false)]
    public void IsOverdue_only_flags_open_orders_past_their_delivery_date(
        PurchaseOrderStatus status,
        int deliveryOffsetInDays,
        bool expected)
    {
        var expectedDelivery = Today.AddDays(deliveryOffsetInDays);

        Assert.Equal(expected, PurchaseOrder.IsOverdue(expectedDelivery, status, Today));
    }

    [Fact]
    public void IsOverdue_is_false_without_an_expected_delivery()
    {
        Assert.False(PurchaseOrder.IsOverdue(null, PurchaseOrderStatus.Ordered, Today));
    }

    [Fact]
    public void LineTotal_multiplies_quantity_by_unit_cost()
    {
        var line = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 2.500m, 19.99m);

        Assert.Equal(49.975m, line.LineTotal());
    }

    [Fact]
    public void OrderTotal_sums_every_line()
    {
        var purchaseOrderId = Guid.NewGuid();

        var lines = new List<PurchaseOrderLine>
        {
            PurchaseOrderLine.Create(purchaseOrderId, Guid.NewGuid(), 3m, 100.50m),
            PurchaseOrderLine.Create(purchaseOrderId, Guid.NewGuid(), 1.250m, 40m),
            PurchaseOrderLine.Create(purchaseOrderId, Guid.NewGuid(), 10m, 0m),
        };

        Assert.Equal(351.50m, PurchaseOrderLine.OrderTotal(lines));
    }

    [Fact]
    public void OrderTotal_of_no_lines_is_zero()
    {
        Assert.Equal(0m, PurchaseOrderLine.OrderTotal([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_line_rejects_a_non_positive_quantity(decimal quantityOrdered)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), quantityOrdered, 10m));
    }

    [Fact]
    public void Create_line_rejects_a_negative_unit_cost()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 1m, -0.01m));
    }

    [Fact]
    public void Create_line_accepts_a_zero_unit_cost()
    {
        var line = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 1m, 0m);

        Assert.Equal(0m, line.UnitCost);
        Assert.Equal(0m, line.LineTotal());
    }

    [Fact]
    public void IncreaseStock_adds_the_received_quantity()
    {
        var item = CreateInventoryItem(7.250m);

        item.IncreaseStock(2.750m);

        Assert.Equal(10.000m, item.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void IncreaseStock_rejects_a_non_positive_quantity(decimal quantityReceived)
    {
        var item = CreateInventoryItem(4m);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.IncreaseStock(quantityReceived));
        Assert.Equal(4m, item.Quantity);
    }

    private static InventoryItem CreateInventoryItem(decimal quantity)
    {
        return InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main engine filter",
            "FILTER-001",
            quantity,
            1m,
            150m);
    }
}
