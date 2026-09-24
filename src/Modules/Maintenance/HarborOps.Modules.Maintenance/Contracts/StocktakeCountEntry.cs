using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record StocktakeCountEntry
{
    public Guid? InventoryItemId { get; init; }

    // null clears the count for the line.
    public decimal? CountedQuantity { get; init; }

    public string? Reason { get; init; }
}
