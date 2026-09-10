using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class GetPreventiveWorkForecastValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get preventive work forecast validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GeneratePreventiveWorkValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Generate preventive work validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class ScheduleOccurrenceNotFoundException(Guid occurrenceId)
    : Exception($"Schedule occurrence '{occurrenceId}' was not found.")
{
    internal Guid OccurrenceId { get; } = occurrenceId;
}

internal sealed class MaintenanceScheduleNotTimeBasedException(Guid scheduleId, string frequency)
    : Exception(
        $"Maintenance schedule '{scheduleId}' uses frequency '{frequency}' and has no supported time-based due date.")
{
    internal Guid ScheduleId { get; } = scheduleId;

    internal string Frequency { get; } = frequency;
}

internal sealed class PreventiveWorkGenerationConflictException(Exception innerException)
    : Exception("Preventive work generation could not be completed because of a concurrent change.", innerException)
{
}

internal sealed class PreventiveWorkReplacementNotAllowedException(
    Guid occurrenceId,
    ScheduleOccurrenceState currentState)
    : Exception(
        $"Schedule occurrence '{occurrenceId}' is '{currentState}' and only cancelled occurrences can be replaced.")
{
    internal Guid OccurrenceId { get; } = occurrenceId;

    internal ScheduleOccurrenceState CurrentState { get; } = currentState;
}

internal sealed class PreventiveWorkScopeMissingException(Guid scheduleId)
    : Exception($"Maintenance schedule '{scheduleId}' has no linked equipment and no resolvable vessel.")
{
    internal Guid ScheduleId { get; } = scheduleId;
}

internal sealed class PreventiveWorkCompletionLinkInvalidException(
    Guid workOrderId,
    Guid scheduleId,
    DateOnly dueDate)
    : Exception(
        $"Work order '{workOrderId}' is not a valid completion for schedule '{scheduleId}' occurrence '{dueDate:yyyy-MM-dd}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal Guid ScheduleId { get; } = scheduleId;

    internal DateOnly DueDate { get; } = dueDate;
}
