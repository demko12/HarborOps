using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record UpdateStocktakeCountsRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public IReadOnlyList<StocktakeCountEntry>? Lines { get; init; }
}
