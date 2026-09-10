using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal static class PreventiveWorkNaming
{
    internal const WorkOrderPriority DefaultPriority = WorkOrderPriority.Medium;

    internal static string Title(string maintenanceType, DateOnly dueDate)
    {
        var title = string.Create(
            CultureInfo.InvariantCulture,
            $"{maintenanceType} - due {dueDate:yyyy-MM-dd}");

        return title.Length <= MaintenanceWorkOrder.TitleMaxLength
            ? title
            : title[..MaintenanceWorkOrder.TitleMaxLength];
    }

    internal static string Description(
        string maintenanceType,
        string frequency,
        Guid scheduleId,
        DateOnly dueDate)
    {
        var description = string.Create(
            CultureInfo.InvariantCulture,
            $"Preventive maintenance '{maintenanceType}' ({frequency}) generated from schedule {scheduleId} for due date {dueDate:yyyy-MM-dd}.");

        return description.Length <= MaintenanceWorkOrder.DescriptionMaxLength
            ? description
            : description[..MaintenanceWorkOrder.DescriptionMaxLength];
    }
}
