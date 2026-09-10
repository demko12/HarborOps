using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GeneratePreventiveWorkValidator
{
    internal const string ThroughField = "through";
    internal const string PriorityField = "priority";

    internal const int MaxWindowDays = 366;

    internal static IReadOnlyDictionary<string, string[]> Validate(
        GeneratePreventiveWorkRequest request,
        DateOnly today)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.Through is null)
        {
            errors[ThroughField] = ["Through date is required."];
        }
        else if (request.Through.Value < today)
        {
            errors[ThroughField] = ["Must be today or later."];
        }
        else if (request.Through.Value.DayNumber - today.DayNumber > MaxWindowDays)
        {
            errors[ThroughField] = [$"The generation window must not exceed {MaxWindowDays} days."];
        }

        if (!string.IsNullOrWhiteSpace(request.Priority) && !TryParsePriority(request.Priority, out _))
        {
            errors[PriorityField] = ["Must be one of Low, Medium, High or Critical."];
        }

        return errors;
    }

    internal static WorkOrderPriority ResolvePriority(string? priority)
    {
        return TryParsePriority(priority, out var parsedPriority)
            ? parsedPriority
            : PreventiveWorkNaming.DefaultPriority;
    }

    private static bool TryParsePriority(string? priority, out WorkOrderPriority parsedPriority)
    {
        return Enum.TryParse(priority, ignoreCase: true, out parsedPriority) && Enum.IsDefined(parsedPriority);
    }
}
