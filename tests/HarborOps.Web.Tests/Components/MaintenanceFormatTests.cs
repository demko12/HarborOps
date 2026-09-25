using HarborOps.Web.Components.Pages.Maintenance;

namespace HarborOps.Web.Tests.Pages;

public sealed class MaintenanceFormatTests
{
    [Fact]
    public void Critical_priority_is_detected_case_sensitively()
    {
        Assert.True(MaintenanceFormat.IsCritical("Critical"));
        Assert.False(MaintenanceFormat.IsCritical("High"));
    }

    [Theory]
    [InlineData(MaintenanceFormat.OpenStatus, true)]
    [InlineData(MaintenanceFormat.InProgressStatus, true)]
    [InlineData(MaintenanceFormat.CompletedStatus, false)]
    [InlineData(MaintenanceFormat.CanceledStatus, false)]
    public void Only_open_and_in_progress_work_orders_can_be_completed(string status, bool expected)
    {
        Assert.Equal(expected, MaintenanceFormat.CanComplete(status));
    }

    [Fact]
    public void A_past_due_open_work_order_is_overdue()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(MaintenanceFormat.IsOverdue(MaintenanceFormat.OpenStatus, utcNow.AddDays(-1), utcNow));
    }

    [Fact]
    public void A_past_due_completed_work_order_is_not_overdue()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(MaintenanceFormat.IsOverdue(MaintenanceFormat.CompletedStatus, utcNow.AddDays(-1), utcNow));
    }

    [Fact]
    public void A_future_due_open_work_order_is_not_overdue()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(MaintenanceFormat.IsOverdue(MaintenanceFormat.OpenStatus, utcNow.AddDays(1), utcNow));
    }

    [Fact]
    public void Missing_completion_date_renders_as_a_placeholder()
    {
        Assert.Equal(MaintenanceFormat.MissingValue, MaintenanceFormat.CompletedUtc(null));
    }

    [Fact]
    public void Missing_assigned_engineer_renders_as_unassigned()
    {
        Assert.Equal("Unassigned", MaintenanceFormat.AssignedEngineer(null));
    }

    [Theory]
    [InlineData(MaintenanceFormat.PendingOccurrence, "Not generated", "pending")]
    [InlineData(MaintenanceFormat.GeneratedOccurrence, "Generated", "generated")]
    [InlineData(MaintenanceFormat.CompletedOccurrence, "Completed", "completed")]
    [InlineData(MaintenanceFormat.CancelledOccurrence, "Cancelled", "cancelled")]
    public void Occurrence_states_have_a_label_and_a_badge_modifier(string state, string label, string modifier)
    {
        Assert.Equal(label, MaintenanceFormat.OccurrenceStateLabel(state));
        Assert.Equal(modifier, MaintenanceFormat.OccurrenceStateModifier(state));
    }

    [Theory]
    [InlineData(MaintenanceFormat.PendingOccurrence, false)]
    [InlineData(MaintenanceFormat.GeneratedOccurrence, false)]
    [InlineData(MaintenanceFormat.CompletedOccurrence, false)]
    [InlineData(MaintenanceFormat.CancelledOccurrence, true)]
    public void Only_cancelled_occurrences_can_be_replaced(string state, bool expected)
    {
        Assert.Equal(expected, MaintenanceFormat.IsReplaceable(state));
    }

    [Theory]
    [InlineData(0, "Generate 0 work orders")]
    [InlineData(1, "Generate 1 work order")]
    [InlineData(3, "Generate 3 work orders")]
    public void The_confirmation_label_counts_the_work_orders(int eligibleCount, string expected)
    {
        Assert.Equal(expected, MaintenanceFormat.GenerateLabel(eligibleCount));
    }

    [Fact]
    public void The_preview_outcome_separates_new_work_from_skipped_work()
    {
        Assert.Equal("Will be created", MaintenanceFormat.PreviewOutcome(MaintenanceFormat.PendingOccurrence));
        Assert.Equal("Skipped: generated", MaintenanceFormat.PreviewOutcome(MaintenanceFormat.GeneratedOccurrence));
        Assert.Equal("Skipped: cancelled", MaintenanceFormat.PreviewOutcome(MaintenanceFormat.CancelledOccurrence));
    }

    [Theory]
    [InlineData("PurchaseOrderNotReceived", "Order not received")]
    [InlineData("NoLines", "No lines")]
    [InlineData("ExceedsReturnable", "Above returnable")]
    [InlineData("InsufficientStock", "Not enough stock")]
    [InlineData("ReservedStock", "Reserved for maintenance")]
    [InlineData("SomethingNew", "SomethingNew")]
    public void Supplier_return_blockers_have_readable_labels(string code, string expected)
    {
        Assert.Equal(expected, MaintenanceFormat.SupplierReturnBlockerLabel(code));
    }

    [Fact]
    public void A_purchase_order_is_referenced_by_its_short_id()
    {
        Assert.Equal(
            "PO 94c1276c",
            MaintenanceFormat.PurchaseOrderReference(Guid.Parse("94c1276c-5459-4d14-adc4-e74a6ee251f4")));
    }

    [Theory]
    [InlineData("through", "Through date")]
    [InlineData("from", "From")]
    [InlineData("to", "To")]
    [InlineData("state", "State")]
    [InlineData("priority", "Priority")]
    public void Preventive_work_fields_have_readable_labels(string field, string expected)
    {
        Assert.Equal(expected, MaintenanceFormat.FieldLabel(field));
    }
}
