using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ReplaceWorkOrderPartReservationsResponse(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<WorkOrderPartReservationListItem> Items,
    bool HasShortages,
    DateTime UpdatedUtc);
