namespace HarborOps.Web.Components.Pages.Maintenance;

internal sealed record PurchaseOrderStatusSlice(string Status, int Count);

internal sealed record PurchaseOrderStatusSegment(string Status, int Count, string DashArray, string DashOffset);
