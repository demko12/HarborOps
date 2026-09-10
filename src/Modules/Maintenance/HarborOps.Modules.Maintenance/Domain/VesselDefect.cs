using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class VesselDefect
{
    internal const int TitleMaxLength = 200;
    internal const int DescriptionMaxLength = 2000;
    internal const int TriageNotesMaxLength = 4000;
    internal const int RejectionReasonMaxLength = 4000;

    private VesselDefect()
    {
    }

    private VesselDefect(
        Guid id,
        Guid vesselId,
        Guid? equipmentId,
        string title,
        string? description,
        DefectSeverity reportedSeverity,
        DateTime observedAtUtc,
        DefectStatus status,
        Guid reportedByUserId,
        DateTime reportedAtUtc,
        DefectSeverity? confirmedSeverity,
        WorkOrderPriority? priority,
        string? triageNotes,
        Guid? triagedByUserId,
        DateTime? triagedAtUtc,
        string? rejectionReason,
        Guid? rejectedByUserId,
        DateTime? rejectedAtUtc,
        Guid? workOrderId,
        Guid? convertedByUserId,
        DateTime? convertedAtUtc,
        Guid concurrencyToken)
    {
        Id = id;
        VesselId = vesselId;
        EquipmentId = equipmentId;
        Title = title;
        Description = description;
        ReportedSeverity = reportedSeverity;
        ObservedAtUtc = observedAtUtc;
        Status = status;
        ReportedByUserId = reportedByUserId;
        ReportedAtUtc = reportedAtUtc;
        ConfirmedSeverity = confirmedSeverity;
        Priority = priority;
        TriageNotes = triageNotes;
        TriagedByUserId = triagedByUserId;
        TriagedAtUtc = triagedAtUtc;
        RejectionReason = rejectionReason;
        RejectedByUserId = rejectedByUserId;
        RejectedAtUtc = rejectedAtUtc;
        WorkOrderId = workOrderId;
        ConvertedByUserId = convertedByUserId;
        ConvertedAtUtc = convertedAtUtc;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public Guid? EquipmentId { get; private set; }

    public string Title { get; private set; } = null!;

    public string? Description { get; private set; }

    public DefectSeverity ReportedSeverity { get; private set; }

    public DateTime ObservedAtUtc { get; private set; }

    public DefectStatus Status { get; private set; }

    public Guid ReportedByUserId { get; private set; }

    public DateTime ReportedAtUtc { get; private set; }

    public DefectSeverity? ConfirmedSeverity { get; private set; }

    public WorkOrderPriority? Priority { get; private set; }

    public string? TriageNotes { get; private set; }

    public Guid? TriagedByUserId { get; private set; }

    public DateTime? TriagedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

    public Guid? RejectedByUserId { get; private set; }

    public DateTime? RejectedAtUtc { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public Guid? ConvertedByUserId { get; private set; }

    public DateTime? ConvertedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal static VesselDefect Register(
        Guid vesselId,
        Guid? equipmentId,
        string title,
        string? description,
        DefectSeverity reportedSeverity,
        DateTime observedAtUtc,
        Guid reportedByUserId,
        DateTime reportedAtUtc)
    {
        return new VesselDefect(
            Guid.NewGuid(),
            vesselId,
            equipmentId,
            title,
            description,
            reportedSeverity,
            observedAtUtc,
            DefectStatus.Reported,
            reportedByUserId,
            reportedAtUtc,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid());
    }

    internal IReadOnlyList<DefectAction> AllowedActions => AllowedActionsFor(Status);

    internal static IReadOnlyList<DefectAction> AllowedActionsFor(DefectStatus status)
    {
        return status switch
        {
            DefectStatus.Reported =>
                [DefectAction.Triage, DefectAction.Reject],
            DefectStatus.Triaged =>
                [DefectAction.Reject, DefectAction.ConvertToWorkOrder],
            _ => [],
        };
    }

    internal void Triage(
        DefectSeverity confirmedSeverity,
        WorkOrderPriority priority,
        string? notes,
        Guid actorUserId,
        DateTime atUtc)
    {
        EnsureAllowed(DefectAction.Triage, DefectStatus.Triaged);

        ConfirmedSeverity = confirmedSeverity;
        Priority = priority;
        TriageNotes = notes;
        TriagedByUserId = actorUserId;
        TriagedAtUtc = atUtc;
        Status = DefectStatus.Triaged;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Reject(string reason, Guid actorUserId, DateTime atUtc)
    {
        EnsureAllowed(DefectAction.Reject, DefectStatus.Rejected);

        RejectionReason = reason;
        RejectedByUserId = actorUserId;
        RejectedAtUtc = atUtc;
        Status = DefectStatus.Rejected;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void ConvertToWorkOrder(Guid workOrderId, Guid actorUserId, DateTime atUtc)
    {
        EnsureAllowed(DefectAction.ConvertToWorkOrder, DefectStatus.Converted);

        WorkOrderId = workOrderId;
        ConvertedByUserId = actorUserId;
        ConvertedAtUtc = atUtc;
        Status = DefectStatus.Converted;
        ConcurrencyToken = Guid.NewGuid();
    }

    private void EnsureAllowed(DefectAction action, DefectStatus requestedStatus)
    {
        if (!AllowedActions.Contains(action))
        {
            throw new DefectTransitionNotAllowedException(Id, Status, requestedStatus);
        }
    }
}
