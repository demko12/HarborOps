using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetMaintenanceWorkOrdersResponse(
    IReadOnlyList<MaintenanceWorkOrderListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
