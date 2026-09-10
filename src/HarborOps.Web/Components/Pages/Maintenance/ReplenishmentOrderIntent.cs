namespace HarborOps.Web.Components.Pages.Maintenance;

/// <summary>
/// A part the planner wants to order from the replenishment board, carried to the purchase order form.
/// </summary>
public sealed record ReplenishmentOrderIntent(
    Guid InventoryItemId,
    string PartNumber,
    decimal Quantity,
    Guid SupplierId);
