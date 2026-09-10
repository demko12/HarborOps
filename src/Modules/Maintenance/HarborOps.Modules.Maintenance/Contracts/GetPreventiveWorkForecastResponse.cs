using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetPreventiveWorkForecastResponse(
    IReadOnlyList<PreventiveWorkOccurrenceItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int PendingCount,
    int GeneratedCount,
    int OverdueCount);
