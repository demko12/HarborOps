using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetScheduleWorkOrdersResponse(
    Guid ScheduleId,
    IReadOnlyList<ScheduleWorkOrderListItem> Items);
