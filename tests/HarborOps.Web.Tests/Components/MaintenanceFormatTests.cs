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
}
