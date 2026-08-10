using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CompleteMaintenanceWorkOrderValidator
{
    internal const string TechnicianIdField = "technicianId";
    internal const string CompletedUtcField = "completedUtc";
    internal const string NotesField = "notes";
    internal const string CostField = "cost";
    internal const string LaborHoursField = "laborHours";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        CompleteMaintenanceWorkOrderRequest request,
        DateTime currentUtc,
        DateTime createdUtc)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.TechnicianId is null || request.TechnicianId == Guid.Empty)
        {
            errors[TechnicianIdField] = ["Technician ID is required."];
        }

        ValidateCompletedUtc(request.CompletedUtc, currentUtc, createdUtc, errors);
        ValidateNotes(request.Notes, errors);
        ValidateCost(request.Cost, errors);
        ValidateLaborHours(request.LaborHours, errors);

        return errors;
    }

    private static void ValidateCompletedUtc(
        DateTime? completedUtc,
        DateTime currentUtc,
        DateTime createdUtc,
        IDictionary<string, string[]> errors)
    {
        if (completedUtc is null)
        {
            errors[CompletedUtcField] = ["Completed UTC is required."];
            return;
        }
        if (completedUtc.Value.Kind != DateTimeKind.Utc)
        {
            errors[CompletedUtcField] = ["Completed UTC must be in UTC format."];
            return;
        }
        if (completedUtc.Value > currentUtc)
        {
            errors[CompletedUtcField] = ["Completed UTC cannot be in the future."];
            return;
        }
        if (completedUtc.Value < createdUtc)
        {
            errors[CompletedUtcField] = ["Completed UTC cannot be before the work order was created."];
            return;
        }
    }

    private static void ValidateNotes(string? notes, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        if (notes.Trim().Length > MaintenanceRecord.NotesMaxLength)
        {
            errors[NotesField] = [$"Notes must not exceed {MaintenanceRecord.NotesMaxLength} characters."];
        }
    }

    private static void ValidateCost(decimal? cost, IDictionary<string, string[]> errors)
    {
        if (cost is not null && cost.Value < 0)
        {
            errors[CostField] = ["Cost must be greater than or equal to zero."];
        }
    }

    private static void ValidateLaborHours(decimal? laborHours, IDictionary<string, string[]> errors)
    {
        if (laborHours is not null && laborHours.Value <= 0)
        {
            errors[LaborHoursField] = ["LaborHours must be greater than zero."];
        }
    }
}
