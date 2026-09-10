using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal static class ScheduleOccurrenceCalculator
{
    internal const int MaxOccurrencesPerSchedule = 120;

    internal static bool IsTimeBased(MaintenanceFrequency frequency)
    {
        return frequency is not MaintenanceFrequency.RunningHours;
    }

    internal static DateOnly Advance(DateOnly anchor, MaintenanceFrequency frequency, int steps)
    {
        return frequency switch
        {
            MaintenanceFrequency.Daily => anchor.AddDays(steps),
            MaintenanceFrequency.Weekly => anchor.AddDays(7 * steps),
            MaintenanceFrequency.Monthly => anchor.AddMonths(steps),
            MaintenanceFrequency.Quarterly => anchor.AddMonths(3 * steps),
            MaintenanceFrequency.SemiAnnual => anchor.AddMonths(6 * steps),
            MaintenanceFrequency.Annual => anchor.AddYears(steps),
            _ => anchor,
        };
    }

    internal static IReadOnlyList<DateOnly> DueDatesInWindow(
        MaintenanceFrequency frequency,
        DateOnly nextDueDate,
        DateOnly from,
        DateOnly to)
    {
        if (!IsTimeBased(frequency) || nextDueDate > to)
        {
            return [];
        }

        var dueDates = new List<DateOnly>();

        for (var step = 0; step < MaxOccurrencesPerSchedule; step++)
        {
            var dueDate = Advance(nextDueDate, frequency, step);

            if (dueDate > to)
            {
                break;
            }

            if (step == 0 || dueDate >= from)
            {
                dueDates.Add(dueDate);
            }
        }

        return dueDates;
    }
}
