using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetWorkOrderPartUsagesResponse(
    IReadOnlyList<WorkOrderPartUsageListItem> Items);
