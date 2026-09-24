using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CreateStocktakeRequest
{
    public IReadOnlyList<Guid>? InventoryItemIds { get; init; }

    public string? Notes { get; init; }
}
