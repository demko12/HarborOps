using HarborOps.Web.Components.Pages.Maintenance;

namespace HarborOps.Web.Tests.Components;

public sealed class ProcurementFormatTests
{
    [Fact]
    public void Money_uses_two_decimals_and_invariant_grouping()
    {
        Assert.Equal("302.00", ProcurementFormat.Money(302m));
        Assert.Equal("1,234.50", ProcurementFormat.Money(1234.5m));
        Assert.Equal("0.00", ProcurementFormat.Money(0m));
    }

    [Fact]
    public void Quantity_keeps_up_to_three_decimals_without_padding()
    {
        Assert.Equal("4", ProcurementFormat.Quantity(4m));
        Assert.Equal("2.5", ProcurementFormat.Quantity(2.500m));
        Assert.Equal("0.125", ProcurementFormat.Quantity(0.125m));
    }

    [Fact]
    public void Dates_are_rendered_as_iso_days()
    {
        Assert.Equal("2026-08-20", ProcurementFormat.OrderDate(new DateOnly(2026, 8, 20)));
        Assert.Equal("2026-09-01", ProcurementFormat.DeliveryDate(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void A_missing_delivery_date_falls_back_to_the_placeholder()
    {
        Assert.Equal(ProcurementFormat.MissingValue, ProcurementFormat.DeliveryDate(null));
    }

    [Fact]
    public void Status_modifiers_are_lowercase_css_suffixes()
    {
        Assert.Equal("draft", ProcurementFormat.StatusModifier(ProcurementFormat.DraftStatus));
        Assert.Equal("received", ProcurementFormat.StatusModifier(ProcurementFormat.ReceivedStatus));
    }

    [Theory]
    [InlineData(true, -3, "Overdue", "overdue")]
    [InlineData(false, 0, "Due today", "scheduled")]
    [InlineData(false, 1, "Due in 1 day", "scheduled")]
    [InlineData(false, 12, "Due in 12 days", "scheduled")]
    public void The_delivery_state_reports_what_the_api_classified(
        bool isOverdue,
        int daysUntilDelivery,
        string expectedText,
        string expectedModifier)
    {
        Assert.Equal(expectedText, ProcurementFormat.DeliveryState(isOverdue, daysUntilDelivery));
        Assert.Equal(expectedModifier, ProcurementFormat.DeliveryStateModifier(isOverdue, daysUntilDelivery));
    }

    [Fact]
    public void An_order_without_a_delivery_date_is_not_treated_as_scheduled()
    {
        Assert.Equal("No delivery date", ProcurementFormat.DeliveryState(false, null));
        Assert.Equal("none", ProcurementFormat.DeliveryStateModifier(false, null));
    }

    [Fact]
    public void A_passed_delivery_date_on_a_closed_order_is_not_reported_as_overdue()
    {
        Assert.Equal("Delivery date passed", ProcurementFormat.DeliveryState(false, -2));
    }

    [Theory]
    [InlineData(ProcurementFormat.OrderedStatus, "Mark as ordered", "default")]
    [InlineData(ProcurementFormat.ReceivedStatus, "Receive stock", "primary")]
    [InlineData(ProcurementFormat.CancelledStatus, "Cancel order", "danger")]
    public void Transitions_carry_a_label_and_a_style_modifier(
        string transition,
        string expectedLabel,
        string expectedModifier)
    {
        Assert.Equal(expectedLabel, ProcurementFormat.TransitionLabel(transition));
        Assert.Equal(expectedModifier, ProcurementFormat.TransitionModifier(transition));
    }

    [Fact]
    public void Receiving_is_confirmed_with_its_stock_consequence()
    {
        var confirmation = ProcurementFormat.TransitionConfirmation(ProcurementFormat.ReceivedStatus);

        Assert.Contains("Inventory quantities will be increased", confirmation, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_totals_treat_missing_input_as_zero()
    {
        Assert.Equal(102m, ProcurementFormat.LineTotal(4m, 25.50m));
        Assert.Equal(0m, ProcurementFormat.LineTotal(null, 25.50m));
        Assert.Equal(0m, ProcurementFormat.LineTotal(4m, null));
    }

    [Theory]
    [InlineData("supplierId", "Supplier")]
    [InlineData("orderDate", "Order date")]
    [InlineData("expectedDelivery", "Expected delivery")]
    [InlineData("lines", "Order lines")]
    [InlineData("contactPerson", "Contact person")]
    public void Header_fields_get_readable_labels(string field, string expected)
    {
        Assert.Equal(expected, ProcurementFormat.FieldLabel(field));
    }

    [Theory]
    [InlineData("lines[0].inventoryItemId", "Line 1 item")]
    [InlineData("lines[2].quantityOrdered", "Line 3 quantity")]
    [InlineData("lines[10].unitCost", "Line 11 unit cost")]
    public void Indexed_line_fields_are_reported_with_a_one_based_line_number(
        string field,
        string expected)
    {
        Assert.Equal(expected, ProcurementFormat.FieldLabel(field));
    }

    [Fact]
    public void An_unknown_field_is_passed_through_unchanged()
    {
        Assert.Equal("somethingElse", ProcurementFormat.FieldLabel("somethingElse"));
    }

    [Theory]
    [InlineData("receivedUtc", "Received at")]
    [InlineData("deliveryReference", "Delivery reference")]
    [InlineData("notes", "Notes")]
    [InlineData("clientRequestId", "Request")]
    [InlineData("expectedConcurrencyToken", "Purchase order version")]
    [InlineData("previewToken", "Receipt check")]
    [InlineData("lines[0].purchaseOrderLineId", "Line 1 order line")]
    [InlineData("lines[1].quantity", "Line 2 quantity")]
    public void Receipt_fields_get_readable_labels(string field, string expected)
    {
        Assert.Equal(expected, ProcurementFormat.FieldLabel(field));
    }

    [Theory]
    [InlineData(ProcurementFormat.PartiallyReceivedStatus, "Partially received")]
    [InlineData(ProcurementFormat.OrderedStatus, "Ordered")]
    [InlineData(ProcurementFormat.ReceivedStatus, "Received")]
    public void Statuses_get_readable_labels(string status, string expected)
    {
        Assert.Equal(expected, ProcurementFormat.StatusLabel(status));
    }

    [Fact]
    public void Partially_received_orders_are_offered_as_a_status_filter()
    {
        Assert.Contains(ProcurementFormat.PartiallyReceivedStatus, ProcurementFormat.Statuses);
        Assert.Equal("partiallyreceived", ProcurementFormat.StatusModifier(ProcurementFormat.PartiallyReceivedStatus));
    }

    [Theory]
    [InlineData(ProcurementFormat.OrderedStatus, 0, 10, "0 / 10")]
    [InlineData(ProcurementFormat.PartiallyReceivedStatus, 4, 10, "4 / 10")]
    [InlineData(ProcurementFormat.ReceivedStatus, 2.5, 2.5, "2.5 / 2.5")]
    [InlineData(ProcurementFormat.DraftStatus, 0, 10, ProcurementFormat.MissingValue)]
    [InlineData(ProcurementFormat.CancelledStatus, 0, 10, ProcurementFormat.MissingValue)]
    public void Received_progress_is_shown_only_for_orders_that_can_receive_goods(
        string status,
        double received,
        double ordered,
        string expected)
    {
        Assert.Equal(expected, ProcurementFormat.ReceivedProgress(status, (decimal)received, (decimal)ordered));
    }

    [Theory]
    [InlineData(0, "0 deliveries")]
    [InlineData(1, "1 delivery")]
    [InlineData(12, "12 deliveries")]
    public void Delivery_counts_are_pluralised(int count, string expected)
    {
        Assert.Equal(expected, ProcurementFormat.DeliveryCount(count));
    }

    [Theory]
    [InlineData(ProcurementFormat.ReceivePartialAction, "Receive delivery")]
    [InlineData(ProcurementFormat.ReceiveRemainingAction, "Receive remaining")]
    [InlineData("SomethingNew", "SomethingNew")]
    public void Receipt_actions_have_readable_labels(string action, string expected)
    {
        Assert.Equal(expected, ProcurementFormat.ReceiptActionLabel(action));
    }

    [Theory]
    [InlineData("OrderNotReceivable", "Order not receivable")]
    [InlineData("NothingOutstanding", "Nothing outstanding")]
    [InlineData("ExceedsOutstanding", "Above outstanding")]
    [InlineData("SomethingNew", "SomethingNew")]
    public void Receipt_blockers_have_readable_labels(string code, string expected)
    {
        Assert.Equal(expected, ProcurementFormat.ReceiptBlockerLabel(code));
    }
}
