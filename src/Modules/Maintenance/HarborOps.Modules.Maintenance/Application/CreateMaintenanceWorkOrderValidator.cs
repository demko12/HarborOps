using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CreateMaintenanceWorkOrderValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string TitleField = "title";
    internal const string DescriptionField = "description";
    internal const string PriorityField = "priority";
    internal const string DueUtcField = "dueUtc";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        CreateMaintenanceWorkOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.VesselId is null || request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = new[] { "Vessel ID is required." };
        }

        ValidateTitle(request.Title, errors);
        ValidateDescription(request.Description, errors);
        ValidatePriority(request.Priority, errors);
        ValidateDueUtc(request.DueUtc, errors);

        return errors;
    }

    private static void ValidateTitle(string? title, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors[TitleField] = [ "Title is required." ];
            return;
        }
        if (title.Length > MaintenanceWorkOrder.TitleMaxLength)
        {
            errors[TitleField] = [ $"Title must not exceed {MaintenanceWorkOrder.TitleMaxLength} characters." ];
        }
    }

    private static void ValidateDescription(string? description, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }
        if (description.Length > MaintenanceWorkOrder.DescriptionMaxLength)
        {
            errors[DescriptionField] = [ $"Description must not exceed {MaintenanceWorkOrder.DescriptionMaxLength} characters." ];
        }
    }

    private static void ValidatePriority(string? priority, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(priority)
            || !Enum.TryParse(priority, ignoreCase: true, out WorkOrderPriority _))
        {
            errors[PriorityField] = ["Priority must be one of: Low, Medium, High, Critical."];
        }
    }

    private static void ValidateDueUtc(DateTime? dueUtc, IDictionary<string, string[]> errors)
    {
        if (dueUtc is null)
        {
            errors[DueUtcField] = ["Due date is required."];
            return;
        }
        if(dueUtc.Value.Kind != DateTimeKind.Utc)
        {
            errors[DueUtcField] = ["Due date must be in UTC format."];
            return;
        }
        if (dueUtc <= DateTime.UtcNow)
        {
            errors[DueUtcField] = ["Due date must be in the future."];
        }
    }
}
