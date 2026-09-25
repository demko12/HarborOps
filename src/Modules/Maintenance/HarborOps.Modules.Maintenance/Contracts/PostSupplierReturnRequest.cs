using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PostSupplierReturnRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }
}
