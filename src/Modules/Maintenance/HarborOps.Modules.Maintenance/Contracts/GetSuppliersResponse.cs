using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetSuppliersResponse(
    IReadOnlyList<SupplierListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
