using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class RegisterMaintenanceScheduleValidator
{
    internal const string MaintenanceTypeField = "maintenanceType";
    internal const string FrequencyField = "frequency";
    internal const string NextDueDateField = "nextDueDate";
    internal const string NextDueHoursField = "nextDueHours";
    internal const string EquipmentIdsField = "equipmentIds";

    internal static readonly string SupportedFrequencies =
        string.Join(", ", Enum.GetNames<MaintenanceFrequency>());

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterMaintenanceScheduleRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateMaintenanceType(request.MaintenanceType, errors);

        var frequency = ValidateFrequency(request.Frequency, errors);

        ValidateDueTriggers(request, frequency, errors);
        ValidateEquipmentIds(request.EquipmentIds, errors);

        return errors;
    }

    private static void ValidateMaintenanceType(string? maintenanceType, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(maintenanceType))
        {
            errors[MaintenanceTypeField] = ["Required."];
            return;
        }

        if (maintenanceType.Trim().Length > MaintenanceSchedule.MaintenanceTypeMaxLength)
        {
            errors[MaintenanceTypeField] =
                [$"Must not exceed {MaintenanceSchedule.MaintenanceTypeMaxLength} characters."];
        }
    }

    private static MaintenanceFrequency? ValidateFrequency(string? frequency, IDictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(frequency)
            && Enum.TryParse(frequency.Trim(), ignoreCase: true, out MaintenanceFrequency parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors[FrequencyField] = [$"Choose one of: {SupportedFrequencies}."];

        return null;
    }

    private static void ValidateDueTriggers(
        RegisterMaintenanceScheduleRequest request,
        MaintenanceFrequency? frequency,
        IDictionary<string, string[]> errors)
    {
        if (request.NextDueDate is null && request.NextDueHours is null)
        {
            errors[NextDueDateField] = ["Enter a next due date, a running-hours threshold, or both."];
        }

        if (request.NextDueHours is { } nextDueHours && nextDueHours < 0)
        {
            errors[NextDueHoursField] = ["Must be zero or greater."];
            return;
        }

        if (frequency is MaintenanceFrequency.RunningHours && request.NextDueHours is null)
        {
            errors[NextDueHoursField] = ["Required for a running-hours schedule."];
        }
    }

    private static void ValidateEquipmentIds(IReadOnlyList<Guid>? equipmentIds, IDictionary<string, string[]> errors)
    {
        if (equipmentIds is null || equipmentIds.Count == 0)
        {
            errors[EquipmentIdsField] = ["Select at least one equipment item."];
            return;
        }

        if (equipmentIds.Any(equipmentId => equipmentId == Guid.Empty))
        {
            errors[EquipmentIdsField] = ["Must not contain empty values."];
            return;
        }

        if (equipmentIds.Distinct().Count() != equipmentIds.Count)
        {
            errors[EquipmentIdsField] = ["Must not contain duplicates."];
        }
    }
}
