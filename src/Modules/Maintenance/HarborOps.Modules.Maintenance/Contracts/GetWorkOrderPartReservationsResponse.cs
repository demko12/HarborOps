using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetWorkOrderPartReservationsResponse(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<WorkOrderPartReservationListItem> Items,
    bool HasShortages,
    DateTime RetrievedUtc);
