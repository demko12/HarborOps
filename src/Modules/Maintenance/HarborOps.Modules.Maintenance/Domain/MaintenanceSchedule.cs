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
        string frequency,
        DateOnly? nextDueDate,
        int? nextDueHours)
    {
        return new MaintenanceSchedule(
            Guid.NewGuid(),
            maintenanceType,
            frequency,
            nextDueDate,
            nextDueHours);
    }
}
