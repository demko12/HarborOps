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
    [InlineData(PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartiallyReceived, false)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Draft, false)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Ordered, false)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.PartiallyReceived, false)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received, true)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Cancelled, false)]
    [InlineData(PurchaseOrderStatus.Received, PurchaseOrderStatus.PartiallyReceived, false)]
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

        Assert.Equal(
            [PurchaseOrderStatus.Received],
            PurchaseOrder.AllowedTransitions(PurchaseOrderStatus.PartiallyReceived));

        Assert.Empty(PurchaseOrder.AllowedTransitions(PurchaseOrderStatus.Received));
        Assert.Empty(PurchaseOrder.AllowedTransitions(PurchaseOrderStatus.Cancelled));
    }

    [Fact]
    public void Status_values_keep_their_persisted_numbers()
    {
        Assert.Equal(0, (int)PurchaseOrderStatus.Draft);
        Assert.Equal(1, (int)PurchaseOrderStatus.Ordered);
        Assert.Equal(2, (int)PurchaseOrderStatus.Received);
        Assert.Equal(3, (int)PurchaseOrderStatus.Cancelled);
        Assert.Equal(4, (int)PurchaseOrderStatus.PartiallyReceived);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, false, false)]
    [InlineData(PurchaseOrderStatus.Ordered, true, false)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, true, true)]
    [InlineData(PurchaseOrderStatus.Received, false, true)]
    [InlineData(PurchaseOrderStatus.Cancelled, false, false)]
    public void CanReceive_and_CanReturn_follow_the_status(
        PurchaseOrderStatus status,
        bool canReceive,
        bool canReturn)
    {
        Assert.Equal(canReceive, PurchaseOrder.CanReceive(status));
        Assert.Equal(canReturn, PurchaseOrder.CanReturn(status));
    }

    [Fact]
    public void ChangeStatusTo_moves_draft_to_ordered_and_rotates_the_token()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        var token = order.ConcurrencyToken;

        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);

        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);
        Assert.NotEqual(Guid.Empty, token);
        Assert.NotEqual(token, order.ConcurrencyToken);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived)]
    public void ChangeStatusTo_never_receives_without_a_receipt(PurchaseOrderStatus target)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        var token = order.ConcurrencyToken;

        var exception = Assert.Throws<PurchaseOrderTransitionException>(() => order.ChangeStatusTo(target));

        Assert.Equal(target, exception.TargetStatus);
        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);
        Assert.Equal(token, order.ConcurrencyToken);
    }

    [Fact]
    public void RecordReceipt_keeps_the_order_partially_received_while_quantity_remains()
    {
        var (order, lines) = CreateOrderedOrder(10m, 2m);
        var token = order.ConcurrencyToken;

        lines[0].Receive(4m);
        order.RecordReceipt(lines);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.NotEqual(token, order.ConcurrencyToken);
        Assert.Equal(4m, lines[0].ReceivedQuantity);
        Assert.Equal(6m, lines[0].OutstandingQuantity);
        Assert.False(lines[0].IsFulfilled);
        Assert.Equal(0m, lines[1].ReceivedQuantity);
    }

    [Fact]
    public void RecordReceipt_closes_the_order_only_when_every_line_is_fulfilled()
    {
        var (order, lines) = CreateOrderedOrder(10m, 2m);

        lines[0].Receive(10m);
        order.RecordReceipt(lines);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);

        lines[1].Receive(2m);
        order.RecordReceipt(lines);

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.All(lines, line => Assert.True(line.IsFulfilled));
        Assert.All(lines, line => Assert.Equal(0m, line.OutstandingQuantity));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void RecordReceipt_rejects_orders_that_cannot_be_received(PurchaseOrderStatus status)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        var line = PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 5m, 1m);
        PurchaseOrderTestData.ApplyStatus(order, status, [line]);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => order.RecordReceipt([line]));

        Assert.Equal(status, order.Status);
    }

    [Fact]
    public void RecordReceipt_rejects_lines_of_another_order()
    {
        var (order, _) = CreateOrderedOrder(5m);
        var foreignLine = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 5m, 1m);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => order.RecordReceipt([foreignLine]));
        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => order.RecordReceipt([]));
        Assert.Equal(PurchaseOrderStatus.Ordered, order.Status);
    }

    [Fact]
    public void Receive_accumulates_across_deliveries()
    {
        var line = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 10m, 1m);

        line.Receive(4m);
        line.Receive(2.5m);

        Assert.Equal(6.5m, line.ReceivedQuantity);
        Assert.Equal(3.5m, line.OutstandingQuantity);
        Assert.Equal(0m, line.LegacyReceivedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Receive_rejects_a_non_positive_quantity(decimal quantity)
    {
        var line = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 10m, 1m);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => line.Receive(quantity));
        Assert.Equal(0m, line.ReceivedQuantity);
    }

    [Fact]
    public void Receive_rejects_cumulative_over_receipt()
    {
        var line = PurchaseOrderLine.Create(Guid.NewGuid(), Guid.NewGuid(), 10m, 1m);
        line.Receive(4m);

        Assert.Throws<PurchaseOrderReceiptInvariantException>(() => line.Receive(6.001m));
        Assert.Equal(4m, line.ReceivedQuantity);
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

    [Theory]
    [InlineData(PurchaseOrderStatus.PartiallyReceived)]
    [InlineData(PurchaseOrderStatus.Received)]
    public void ChangeStatusTo_rejects_cancelling_once_goods_were_received(PurchaseOrderStatus status)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        PurchaseOrderTestData.ApplyStatus(order, status, [PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), 4m, 1m)]);

        Assert.Throws<PurchaseOrderTransitionException>(
            () => order.ChangeStatusTo(PurchaseOrderStatus.Cancelled));

        Assert.Equal(status, order.Status);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, -1, true)]
    [InlineData(PurchaseOrderStatus.Ordered, -1, true)]
    [InlineData(PurchaseOrderStatus.Ordered, 0, false)]
    [InlineData(PurchaseOrderStatus.Ordered, 1, false)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, -1, true)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, 0, false)]
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

    private static (PurchaseOrder Order, IReadOnlyList<PurchaseOrderLine> Lines) CreateOrderedOrder(
        params decimal[] quantities)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Today, null);
        order.ChangeStatusTo(PurchaseOrderStatus.Ordered);

        var lines = quantities
            .Select(quantity => PurchaseOrderLine.Create(order.Id, Guid.NewGuid(), quantity, 1m))
            .ToList();

        return (order, lines);
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
