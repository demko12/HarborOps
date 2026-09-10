using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class VesselDefectDomainTests
{
    private static readonly DateTime ObservedAtUtc = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReportedAtUtc = new(2026, 8, 1, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TriagedAtUtc = new(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RejectedAtUtc = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ConvertedAtUtc = new(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc);

    private static VesselDefect CreateReported(
        Guid? vesselId = null,
        Guid? equipmentId = null,
        Guid? reportedByUserId = null)
    {
        return VesselDefect.Register(
            vesselId ?? Guid.NewGuid(),
            equipmentId,
            "Hydraulic leak on crane boom",
            "Visible oil leak near the boom cylinder",
            DefectSeverity.Medium,
            ObservedAtUtc,
            reportedByUserId ?? Guid.NewGuid(),
            ReportedAtUtc);
    }

    private static VesselDefect CreateTriaged(
        DefectSeverity confirmedSeverity = DefectSeverity.High,
        WorkOrderPriority priority = WorkOrderPriority.High,
        string? notes = "Confirmed active leak, needs urgent repair",
        Guid? actorUserId = null)
    {
        var defect = CreateReported();

        defect.Triage(confirmedSeverity, priority, notes, actorUserId ?? Guid.NewGuid(), TriagedAtUtc);

        return defect;
    }

    private static VesselDefect CreateConverted(Guid? workOrderId = null, Guid? actorUserId = null)
    {
        var defect = CreateTriaged();

        defect.ConvertToWorkOrder(workOrderId ?? Guid.NewGuid(), actorUserId ?? Guid.NewGuid(), ConvertedAtUtc);

        return defect;
    }

    private static VesselDefect CreateRejectedFromReported(Guid? actorUserId = null)
    {
        var defect = CreateReported();

        defect.Reject("Duplicate report", actorUserId ?? Guid.NewGuid(), RejectedAtUtc);

        return defect;
    }

    // ---- Register ----

    [Fact]
    public void Register_produces_a_reported_defect_with_the_given_fields()
    {
        var vesselId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var reportedByUserId = Guid.NewGuid();

        var defect = VesselDefect.Register(
            vesselId,
            equipmentId,
            "Hydraulic leak on crane boom",
            "Visible oil leak near the boom cylinder",
            DefectSeverity.Medium,
            ObservedAtUtc,
            reportedByUserId,
            ReportedAtUtc);

        Assert.NotEqual(Guid.Empty, defect.Id);
        Assert.Equal(vesselId, defect.VesselId);
        Assert.Equal(equipmentId, defect.EquipmentId);
        Assert.Equal("Hydraulic leak on crane boom", defect.Title);
        Assert.Equal("Visible oil leak near the boom cylinder", defect.Description);
        Assert.Equal(DefectSeverity.Medium, defect.ReportedSeverity);
        Assert.Equal(ObservedAtUtc, defect.ObservedAtUtc);
        Assert.Equal(DefectStatus.Reported, defect.Status);
        Assert.Equal(reportedByUserId, defect.ReportedByUserId);
        Assert.Equal(ReportedAtUtc, defect.ReportedAtUtc);
        Assert.Null(defect.ConfirmedSeverity);
        Assert.Null(defect.Priority);
        Assert.Null(defect.TriageNotes);
        Assert.Null(defect.WorkOrderId);
        Assert.NotEqual(Guid.Empty, defect.ConcurrencyToken);
    }

    [Fact]
    public void Register_allows_a_null_equipment_id()
    {
        var defect = CreateReported(equipmentId: null);

        Assert.Null(defect.EquipmentId);
    }

    // ---- Triage ----

    [Fact]
    public void Triage_from_reported_succeeds_and_records_every_field()
    {
        var defect = CreateReported();
        var tokenBeforeTriage = defect.ConcurrencyToken;
        var actorUserId = Guid.NewGuid();

        defect.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, "Escalated on inspection", actorUserId, TriagedAtUtc);

        Assert.Equal(DefectStatus.Triaged, defect.Status);
        Assert.Equal(DefectSeverity.Critical, defect.ConfirmedSeverity);
        Assert.Equal(WorkOrderPriority.Critical, defect.Priority);
        Assert.Equal("Escalated on inspection", defect.TriageNotes);
        Assert.Equal(actorUserId, defect.TriagedByUserId);
        Assert.Equal(TriagedAtUtc, defect.TriagedAtUtc);
        Assert.NotEqual(tokenBeforeTriage, defect.ConcurrencyToken);
    }

    [Fact]
    public void Triage_accepts_null_notes()
    {
        var defect = CreateReported();

        defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), TriagedAtUtc);

        Assert.Null(defect.TriageNotes);
    }

    [Fact]
    public void Triage_from_converted_throws_the_transition_guard()
    {
        var defect = CreateConverted();

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), TriagedAtUtc));

        Assert.Equal(defect.Id, exception.DefectId);
        Assert.Equal(DefectStatus.Converted, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Triaged, exception.RequestedStatus);
    }

    [Fact]
    public void Triage_from_rejected_throws_the_transition_guard()
    {
        var defect = CreateRejectedFromReported();

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), TriagedAtUtc));

        Assert.Equal(DefectStatus.Rejected, exception.CurrentStatus);
    }

    [Fact]
    public void Triage_called_a_second_time_on_an_already_triaged_defect_throws()
    {
        var defect = CreateTriaged();

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.Triage(DefectSeverity.Low, WorkOrderPriority.Low, "Second triage attempt", Guid.NewGuid(), TriagedAtUtc.AddHours(1)));

        Assert.Equal(defect.Id, exception.DefectId);
        Assert.Equal(DefectStatus.Triaged, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Triaged, exception.RequestedStatus);
    }

    // ---- Reject ----

    [Fact]
    public void Reject_from_reported_succeeds_and_records_every_field()
    {
        var defect = CreateReported();
        var tokenBeforeReject = defect.ConcurrencyToken;
        var actorUserId = Guid.NewGuid();

        defect.Reject("No longer applicable", actorUserId, RejectedAtUtc);

        Assert.Equal(DefectStatus.Rejected, defect.Status);
        Assert.Equal("No longer applicable", defect.RejectionReason);
        Assert.Equal(actorUserId, defect.RejectedByUserId);
        Assert.Equal(RejectedAtUtc, defect.RejectedAtUtc);
        Assert.NotEqual(tokenBeforeReject, defect.ConcurrencyToken);
    }

    [Fact]
    public void Reject_from_triaged_succeeds()
    {
        var defect = CreateTriaged();
        var actorUserId = Guid.NewGuid();

        defect.Reject("Assessed as low priority, not worth a work order", actorUserId, RejectedAtUtc);

        Assert.Equal(DefectStatus.Rejected, defect.Status);
        Assert.Equal(actorUserId, defect.RejectedByUserId);

        // Priority/ConfirmedSeverity from the earlier Triage are not cleared by Reject -
        // they remain as the historical record of what triage concluded before rejection.
        Assert.Equal(DefectSeverity.High, defect.ConfirmedSeverity);
        Assert.Equal(WorkOrderPriority.High, defect.Priority);
    }

    [Fact]
    public void Reject_from_converted_throws_the_transition_guard()
    {
        var defect = CreateConverted();

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.Reject("Too late", Guid.NewGuid(), RejectedAtUtc));

        Assert.Equal(DefectStatus.Converted, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Rejected, exception.RequestedStatus);
    }

    [Fact]
    public void Reject_from_rejected_throws_the_transition_guard()
    {
        var defect = CreateRejectedFromReported();

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.Reject("Rejecting again", Guid.NewGuid(), RejectedAtUtc.AddHours(1)));

        Assert.Equal(DefectStatus.Rejected, exception.CurrentStatus);
    }

    [Fact]
    public void Reject_accepts_an_empty_reason_at_the_domain_level()
    {
        // Non-empty reason is an Application-layer validator concern (RejectDefectValidator),
        // not a Domain invariant - VesselDefect.Reject itself has no reason-shape guard, matching
        // MaintenanceWorkOrder.Create's own precedent of not validating input in Domain. This is
        // expected-per-design, not a gap: see RejectDefectValidatorTests for the enforced rule.
        var defect = CreateReported();

        defect.Reject(string.Empty, Guid.NewGuid(), RejectedAtUtc);

        Assert.Equal(DefectStatus.Rejected, defect.Status);
        Assert.Equal(string.Empty, defect.RejectionReason);
    }

    // ---- ConvertToWorkOrder ----

    [Fact]
    public void ConvertToWorkOrder_from_triaged_succeeds_and_records_every_field()
    {
        var defect = CreateTriaged();
        var tokenBeforeConvert = defect.ConcurrencyToken;
        var workOrderId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();

        defect.ConvertToWorkOrder(workOrderId, actorUserId, ConvertedAtUtc);

        Assert.Equal(DefectStatus.Converted, defect.Status);
        Assert.Equal(workOrderId, defect.WorkOrderId);
        Assert.Equal(actorUserId, defect.ConvertedByUserId);
        Assert.Equal(ConvertedAtUtc, defect.ConvertedAtUtc);
        Assert.NotEqual(tokenBeforeConvert, defect.ConcurrencyToken);
    }

    [Fact]
    public void ConvertToWorkOrder_from_reported_throws_because_it_was_never_triaged()
    {
        var defect = CreateReported();

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.ConvertToWorkOrder(Guid.NewGuid(), Guid.NewGuid(), ConvertedAtUtc));

        Assert.Equal(defect.Id, exception.DefectId);
        Assert.Equal(DefectStatus.Reported, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Converted, exception.RequestedStatus);
    }

    [Fact]
    public void ConvertToWorkOrder_from_rejected_throws_the_transition_guard()
    {
        var defect = CreateRejectedFromReported();

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.ConvertToWorkOrder(Guid.NewGuid(), Guid.NewGuid(), ConvertedAtUtc));

        Assert.Equal(DefectStatus.Rejected, exception.CurrentStatus);
    }

    [Fact]
    public void A_second_conversion_attempt_on_an_already_converted_defect_throws()
    {
        var firstWorkOrderId = Guid.NewGuid();
        var defect = CreateConverted(workOrderId: firstWorkOrderId);

        var exception = Assert.Throws<DefectTransitionNotAllowedException>(
            () => defect.ConvertToWorkOrder(Guid.NewGuid(), Guid.NewGuid(), ConvertedAtUtc.AddHours(1)));

        Assert.Equal(defect.Id, exception.DefectId);
        Assert.Equal(DefectStatus.Converted, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Converted, exception.RequestedStatus);

        // The original linkage from the first, successful conversion must survive
        // the rejected second attempt untouched - this is the "at most one work
        // order" invariant, enforced purely by the transition guard above.
        Assert.Equal(firstWorkOrderId, defect.WorkOrderId);
    }

    // ---- AllowedActions ----

    [Theory]
    [InlineData(DefectStatus.Reported, new[] { DefectAction.Triage, DefectAction.Reject })]
    [InlineData(DefectStatus.Triaged, new[] { DefectAction.Reject, DefectAction.ConvertToWorkOrder })]
    [InlineData(DefectStatus.Converted, new DefectAction[0])]
    [InlineData(DefectStatus.Rejected, new DefectAction[0])]
    public void The_action_matrix_offers_only_the_supported_moves(DefectStatus status, DefectAction[] expected)
    {
        Assert.Equal(expected, VesselDefect.AllowedActionsFor(status));
    }
}
