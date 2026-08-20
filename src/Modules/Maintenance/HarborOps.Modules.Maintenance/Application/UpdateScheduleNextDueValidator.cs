using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class UpdateScheduleNextDueValidator
{
    internal const string CompletedOnField = "completedOn";
    internal const string NextDueHoursField = "nextDueHours";
    internal const string FrequencyField = "frequency";

    internal static IReadOnlyDictionary<string, string[]> UnsupportedFrequency(string storedFrequency)
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [FrequencyField] =
            [
                $"This schedule stores an unsupported frequency '{storedFrequency}'. "
                + $"Supported values are: {RegisterMaintenanceScheduleValidator.SupportedFrequencies}.",
            ],
        };
    }

    internal static IReadOnlyDictionary<string, string[]> Validate(UpdateScheduleNextDueRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.CompletedOn is null)
        {
            errors[CompletedOnField] = ["Required."];
        }

        if (request.NextDueHours is { } nextDueHours && nextDueHours < 0)
        {
            errors[NextDueHoursField] = ["Must be zero or greater."];
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateAgainstFrequency(
        UpdateScheduleNextDueRequest request,
        MaintenanceFrequency frequency)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (frequency is MaintenanceFrequency.RunningHours && request.NextDueHours is null)
        {
            errors[NextDueHoursField] = ["Required for a running-hours schedule."];
        }

        return errors;
    }
}
