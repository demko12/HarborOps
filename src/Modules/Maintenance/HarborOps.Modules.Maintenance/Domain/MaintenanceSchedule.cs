using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class MaintenanceSchedule
{
    internal const int MaintenanceTypeMaxLength = 100;
    internal const int FrequencyMaxLength = 100;

    private MaintenanceSchedule()
    {
    }

    private MaintenanceSchedule(
        Guid id,
        string maintenanceType,
        string frequency,
        DateOnly? nextDueDate,
        int? nextDueHours)
    {
        Id = id;
        MaintenanceType = maintenanceType;
        Frequency = frequency;
        NextDueDate = nextDueDate;
        NextDueHours = nextDueHours;
    }

    public Guid Id { get; private set; }

    public string MaintenanceType { get; private set; } = null!;

    public string Frequency { get; private set; } = null!;

    public DateOnly? NextDueDate { get; private set; }

    public int? NextDueHours { get; private set; }

    internal static MaintenanceSchedule Create(
        string maintenanceType,
        MaintenanceFrequency frequency,
        DateOnly? nextDueDate,
        int? nextDueHours)
    {
        return new MaintenanceSchedule(
            Guid.NewGuid(),
            maintenanceType,
            frequency.ToString(),
            nextDueDate,
            nextDueHours);
    }

    internal static bool IsOverdue(DateOnly? nextDueDate, DateOnly today)
    {
        return nextDueDate is { } dueDate && dueDate < today;
    }

    internal static int? DaysUntilDue(DateOnly? nextDueDate, DateOnly today)
    {
        return nextDueDate is { } dueDate ? dueDate.DayNumber - today.DayNumber : null;
    }

    internal bool TryGetFrequency(out MaintenanceFrequency frequency)
    {
        return Enum.TryParse(Frequency, ignoreCase: true, out frequency)
            && Enum.IsDefined(frequency);
    }

    internal void RecordCompletion(MaintenanceFrequency frequency, DateOnly completedOn, int? nextDueHours)
    {
        NextDueDate = NextDueDateAfter(completedOn, frequency) ?? NextDueDate;

        if (nextDueHours.HasValue)
        {
            NextDueHours = nextDueHours.Value;
        }
    }

    private static DateOnly? NextDueDateAfter(DateOnly completedOn, MaintenanceFrequency frequency)
    {
        return frequency switch
        {
            MaintenanceFrequency.Daily => completedOn.AddDays(1),
            MaintenanceFrequency.Weekly => completedOn.AddDays(7),
            MaintenanceFrequency.Monthly => completedOn.AddMonths(1),
            MaintenanceFrequency.Quarterly => completedOn.AddMonths(3),
            MaintenanceFrequency.SemiAnnual => completedOn.AddMonths(6),
            MaintenanceFrequency.Annual => completedOn.AddYears(1),
            _ => null,
        };
    }
}
