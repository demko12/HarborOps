using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PostStocktakeRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }
}
