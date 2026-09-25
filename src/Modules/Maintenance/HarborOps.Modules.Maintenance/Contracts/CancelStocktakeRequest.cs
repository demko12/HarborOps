using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CancelStocktakeRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? Reason { get; init; }
}
