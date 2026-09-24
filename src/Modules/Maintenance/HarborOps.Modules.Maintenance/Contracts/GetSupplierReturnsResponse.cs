using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetSupplierReturnsResponse(
    IReadOnlyList<SupplierReturnListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
