using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RefreshStocktakeLinesRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public IReadOnlyList<Guid>? InventoryItemIds { get; init; }
}
