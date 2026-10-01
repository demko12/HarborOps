using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselPositionsResponse(
    IReadOnlyList<VesselPositionListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    double StaleAfterHours,
    DateTime EvaluatedAtUtc,
    AisTrackingStatus? AisTracking = null);
