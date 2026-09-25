using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetStocktakesRequest
{
    public string? Status { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}

