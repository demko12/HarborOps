using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class DryDockTransitionResult
{
    internal static DryDockTransitionResponse From(DryDock dryDock)
    {
        return new DryDockTransitionResponse(
            dryDock.Id,
            dryDock.Status.ToString(),
            dryDock.AllowedNextStatuses.Select(status => status.ToString()).ToList(),
            dryDock.EndDate,
            dryDock.ActualCost);
    }
}
