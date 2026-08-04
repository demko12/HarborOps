using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class MaintenanceRecord
{
    internal const int NotesMaxLength = 4000;

    private MaintenanceRecord()
    {
    }

    private MaintenanceRecord(
        Guid id,
        Guid workOrderId,
        Guid technicianId,
        DateOnly completionDate,
        string? notes,
        decimal? cost,
        decimal? laborHours)
    {
        Id = id;
        WorkOrderId = workOrderId;
        TechnicianId = technicianId;
        CompletionDate = completionDate;
        Notes = notes;
        Cost = cost;
        LaborHours = laborHours;
    }

    public Guid Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public DateOnly CompletionDate { get; private set; }

    public string? Notes { get; private set; }

    public decimal? Cost { get; private set; }

    public decimal? LaborHours { get; private set; }

    internal static MaintenanceRecord Create(
        Guid workOrderId,
        Guid technicianId,
        DateOnly completionDate,
        string? notes,
        decimal? cost,
        decimal? laborHours)
    {
        return new MaintenanceRecord(
            Guid.NewGuid(),
            workOrderId,
            technicianId,
            completionDate,
            notes,
            cost,
            laborHours);
    }
}
