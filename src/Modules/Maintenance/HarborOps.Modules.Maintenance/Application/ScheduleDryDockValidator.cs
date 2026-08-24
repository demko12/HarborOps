using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class ScheduleDryDockValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string PortIdField = "portId";
    internal const string StartDateField = "startDate";
    internal const string PlannedTasksField = "plannedTasks";

    internal static IReadOnlyDictionary<string, string[]> Validate(ScheduleDryDockRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId is null || request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Select a vessel."];
        }

        if (request.PortId is null || request.PortId == Guid.Empty)
        {
            errors[PortIdField] = ["Select a port."];
        }

        if (request.StartDate is null)
        {
            errors[StartDateField] = ["Required."];
        }

        if (string.IsNullOrWhiteSpace(request.PlannedTasks))
        {
            errors[PlannedTasksField] = ["Required."];
        }
        else if (request.PlannedTasks.Trim().Length > DryDock.PlannedTasksMaxLength)
        {
            errors[PlannedTasksField] =
                [$"Must not exceed {DryDock.PlannedTasksMaxLength} characters."];
        }

        return errors;
    }
}
