using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class ContainerControlTests
{
    private static readonly Guid ContainerId =
        Guid.Parse("3f2a1b0c-9d8e-4f7a-8b6c-5d4e3f2a1b0c");

    private static readonly Guid InspectorUserId =
        Guid.Parse("7c6b5a49-3827-4165-9483-2a1b0c9d8e7f");

    private static readonly Guid SupervisorUserId =
        Guid.Parse("2e1d0c9b-8a79-4685-9473-1c0b9a8d7e6f");

    private static readonly DateTime SealedAtUtc =
        new(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime InspectedAtUtc =
        new(2026, 9, 7, 9, 15, 0, DateTimeKind.Utc);

    private static readonly DateTime ReleasedAtUtc =
        new(2026, 9, 7, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Start_leaves_the_container_pending_without_a_seal()
    {
        var control = ContainerControl.Start(ContainerId);

        Assert.Equal(ContainerId, control.ContainerId);
        Assert.Equal(ContainerControlState.Pending, control.State);
        Assert.Equal(ContainerSealStatus.None, control.SealStatus);
        Assert.Null(control.SealNumber);
        Assert.Null(control.LatestInspectionResult);
        Assert.Null(control.StateReason);
    }

    [Fact]
    public void AssignSeal_normalizes_the_seal_number_and_marks_it_intact()
    {
        var control = ContainerControl.Start(ContainerId);

        var entry = control.AssignSeal("  ml 45 8821  ", InspectorUserId, SealedAtUtc);

        Assert.Equal("ML458821", control.SealNumber);
        Assert.Equal(ContainerSealStatus.Intact, control.SealStatus);
        Assert.Equal(ContainerControlAction.SealAssigned, entry.Action);
        Assert.Equal("ML458821", entry.SealNumber);
        Assert.Equal(InspectorUserId, entry.ActorUserId);
        Assert.Equal(SealedAtUtc, entry.OccurredUtc);
    }

    [Fact]
    public void AssignSeal_replaces_a_broken_seal_with_an_intact_one()
    {
        var control = ContainerControl.Start(ContainerId);
        control.AssignSeal("ML458821", InspectorUserId, SealedAtUtc);
        control.BreakSeal(InspectorUserId, InspectedAtUtc, null);

        control.AssignSeal("ML999000", InspectorUserId, ReleasedAtUtc);

        Assert.Equal("ML999000", control.SealNumber);
        Assert.Equal(ContainerSealStatus.Intact, control.SealStatus);
    }

    [Fact]
    public void AssignSeal_revokes_a_release_when_the_seal_is_replaced()
    {
        var control = Released();

        control.AssignSeal("ML999000", InspectorUserId, ReleasedAtUtc);

        Assert.Equal("ML999000", control.SealNumber);
        Assert.Equal(ContainerControlState.Pending, control.State);
        Assert.Null(control.StateReason);
        Assert.Equal(InspectorUserId, control.StateChangedByUserId);
    }

    [Fact]
    public void AssignSeal_is_rejected_when_the_same_seal_is_already_intact()
    {
        var control = Sealed();

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.AssignSeal("  ml 458 821  ", InspectorUserId, ReleasedAtUtc));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealAlreadyAssignedReason,
            exception.Reason);
    }

    [Fact]
    public void AssignSeal_keeps_a_release_when_the_same_seal_is_re_submitted()
    {
        var control = Released();

        Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.AssignSeal("ML458821", InspectorUserId, ReleasedAtUtc));

        Assert.Equal(ContainerControlState.Released, control.State);
    }

    [Fact]
    public void BreakSeal_marks_the_seal_broken()
    {
        var control = Sealed();

        var entry = control.BreakSeal(InspectorUserId, InspectedAtUtc, "  Opened for customs  ");

        Assert.Equal(ContainerSealStatus.Broken, control.SealStatus);
        Assert.Equal(ContainerControlAction.SealBroken, entry.Action);
        Assert.Equal("Opened for customs", entry.Notes);
    }

    [Fact]
    public void BreakSeal_is_rejected_when_no_intact_seal_exists()
    {
        var control = ContainerControl.Start(ContainerId);

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.BreakSeal(InspectorUserId, InspectedAtUtc, null));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
            exception.Reason);
    }

    [Fact]
    public void BreakSeal_revokes_an_existing_release()
    {
        var control = Released();

        control.BreakSeal(SupervisorUserId, ReleasedAtUtc, null);

        Assert.Equal(ContainerControlState.Pending, control.State);
        Assert.Null(control.StateReason);
    }

    [Theory]
    [InlineData(nameof(ContainerInspectionResult.Passed))]
    [InlineData(nameof(ContainerInspectionResult.Failed))]
    public void RecordInspection_stores_the_outcome_and_the_server_timestamp(
        string resultName)
    {
        var result = Enum.Parse<ContainerInspectionResult>(resultName);
        var control = Sealed();

        var entry = control.RecordInspection(
            result,
            "  Doors aligned  ",
            null,
            isReefer: false,
            InspectorUserId,
            InspectedAtUtc)[0];

        Assert.Equal(result, control.LatestInspectionResult);
        Assert.Equal("Doors aligned", control.LatestInspectionNotes);
        Assert.Equal(InspectedAtUtc, control.LatestInspectedAtUtc);
        Assert.Equal(InspectorUserId, control.LatestInspectedByUserId);
        Assert.Equal(ContainerControlAction.InspectionRecorded, entry.Action);
        Assert.Equal(result, entry.InspectionResult);
    }

    [Fact]
    public void RecordInspection_accepts_a_temperature_for_a_reefer_container()
    {
        var control = Sealed();

        control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Cooling steady",
            -18.5m,
            isReefer: true,
            InspectorUserId,
            InspectedAtUtc);

        Assert.Equal(-18.5m, control.LatestInspectionTemperatureCelsius);
    }

    [Fact]
    public void RecordInspection_rejects_a_temperature_for_a_dry_container()
    {
        var control = Sealed();

        Assert.Throws<ContainerTemperatureNotAllowedException>(
            () => control.RecordInspection(
                ContainerInspectionResult.Passed,
                "Cooling steady",
                -18.5m,
                isReefer: false,
                InspectorUserId,
                InspectedAtUtc));

        Assert.Null(control.LatestInspectionResult);
    }

    [Fact]
    public void RecordInspection_keeps_a_reefer_temperature_optional()
    {
        var control = Sealed();

        control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Cooling steady",
            null,
            isReefer: true,
            InspectorUserId,
            InspectedAtUtc);

        Assert.Null(control.LatestInspectionTemperatureCelsius);
        Assert.Equal(ContainerInspectionResult.Passed, control.LatestInspectionResult);
    }

    [Fact]
    public void RecordInspection_revokes_a_release_when_the_container_fails()
    {
        var control = Released();

        var entry = control.RecordInspection(
            ContainerInspectionResult.Failed,
            "Door seal damaged",
            null,
            isReefer: false,
            InspectorUserId,
            ReleasedAtUtc)[0];

        Assert.Equal(ContainerControlState.Pending, control.State);
        Assert.Equal(ContainerControlState.Released, entry.FromState);
        Assert.Equal(ContainerControlState.Pending, entry.ToState);
    }

    [Fact]
    public void RecordInspection_keeps_a_release_when_the_container_passes_again()
    {
        var control = Released();

        control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Re-checked",
            null,
            isReefer: false,
            InspectorUserId,
            ReleasedAtUtc);

        Assert.Equal(ContainerControlState.Released, control.State);
    }

    [Fact]
    public void RecordInspection_clears_a_hold_when_the_container_passes()
    {
        var control = Sealed();
        control.Hold("Damaged corner post", SupervisorUserId, InspectedAtUtc);

        var entries = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Corner post repaired",
            null,
            isReefer: false,
            InspectorUserId,
            ReleasedAtUtc);

        Assert.Equal(ContainerControlState.Pending, control.State);
        Assert.Null(control.StateReason);
        Assert.Equal(2, entries.Count);
        Assert.Equal(ContainerControlAction.InspectionRecorded, entries[0].Action);
        Assert.Equal(ContainerControlAction.HoldLifted, entries[1].Action);
    }

    [Fact]
    public void Lifting_a_hold_records_the_transition_and_keeps_the_reason()
    {
        var control = Sealed();
        control.Hold("Damaged corner post", SupervisorUserId, InspectedAtUtc);

        var lifted = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Corner post repaired",
            null,
            isReefer: false,
            InspectorUserId,
            ReleasedAtUtc)[1];

        Assert.Equal(ContainerControlState.Held, lifted.FromState);
        Assert.Equal(ContainerControlState.Pending, lifted.ToState);
        Assert.Equal("Damaged corner post", lifted.Notes);
        Assert.Equal(InspectorUserId, lifted.ActorUserId);
        Assert.Equal(ReleasedAtUtc, lifted.OccurredUtc);
    }

    [Fact]
    public void An_inspection_records_a_single_entry_when_no_hold_is_lifted()
    {
        var control = Sealed();

        var entries = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Doors aligned",
            null,
            isReefer: false,
            InspectorUserId,
            InspectedAtUtc);

        Assert.Single(entries);
        Assert.Equal(ContainerControlAction.InspectionRecorded, entries[0].Action);
    }

    [Fact]
    public void RecordInspection_keeps_a_hold_when_the_container_fails_again()
    {
        var control = Sealed();
        control.Hold("Damaged corner post", SupervisorUserId, InspectedAtUtc);

        control.RecordInspection(
            ContainerInspectionResult.Failed,
            "Still damaged",
            null,
            isReefer: false,
            InspectorUserId,
            ReleasedAtUtc);

        Assert.Equal(ContainerControlState.Held, control.State);
        Assert.Equal("Damaged corner post", control.StateReason);
    }

    [Fact]
    public void Release_records_the_actor_and_the_timestamp()
    {
        var control = Inspected(ContainerInspectionResult.Passed);

        var entry = control.Release(SupervisorUserId, ReleasedAtUtc, "  Cleared for loading  ", false);

        Assert.Equal(ContainerControlState.Released, control.State);
        Assert.Equal("Cleared for loading", control.StateReason);
        Assert.Equal(SupervisorUserId, control.StateChangedByUserId);
        Assert.Equal(ReleasedAtUtc, control.StateChangedAtUtc);
        Assert.Equal(ContainerControlAction.Released, entry.Action);
        Assert.Equal(ContainerControlState.Pending, entry.FromState);
        Assert.Equal(ContainerControlState.Released, entry.ToState);
    }

    [Fact]
    public void Release_is_rejected_without_an_intact_seal()
    {
        var control = ContainerControl.Start(ContainerId);
        control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Sound",
            null,
            isReefer: false,
            InspectorUserId,
            InspectedAtUtc);

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, false));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
            exception.Reason);
        Assert.Equal(ContainerControlState.Pending, control.State);
    }

    [Fact]
    public void Release_is_rejected_when_the_seal_is_broken()
    {
        var control = Inspected(ContainerInspectionResult.Passed);
        control.BreakSeal(InspectorUserId, InspectedAtUtc, null);

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, false));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.SealNotIntactReason,
            exception.Reason);
    }

    [Fact]
    public void Release_is_rejected_without_an_inspection()
    {
        var control = Sealed();

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, false));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.InspectionMissingReason,
            exception.Reason);
    }

    [Fact]
    public void An_uninspected_container_is_told_it_still_needs_an_inspection()
    {
        var control = Sealed();

        Assert.False(control.CanRelease(hasOpenTemperatureExcursion: false));
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.InspectionMissingReason,
            control.ReleaseBlocker(hasOpenTemperatureExcursion: false));
    }

    [Fact]
    public void Release_is_rejected_when_the_latest_inspection_failed()
    {
        var control = Inspected(ContainerInspectionResult.Failed);

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, false));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.InspectionNotPassedReason,
            exception.Reason);
    }

    [Fact]
    public void Release_is_rejected_while_the_container_is_held()
    {
        var control = Inspected(ContainerInspectionResult.Passed);
        control.Hold("Awaiting supervisor", SupervisorUserId, InspectedAtUtc);

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, false));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.HeldReason,
            exception.Reason);
        Assert.Equal(ContainerControlState.Held, control.State);
    }

    [Fact]
    public void Release_is_rejected_when_the_container_is_already_released()
    {
        var control = Released();

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, false));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.AlreadyReleasedReason,
            exception.Reason);
    }

    [Fact]
    public void Hold_stores_the_reason_and_the_actor()
    {
        var control = Sealed();

        var entry = control.Hold("  Damaged corner post  ", SupervisorUserId, InspectedAtUtc);

        Assert.Equal(ContainerControlState.Held, control.State);
        Assert.Equal("Damaged corner post", control.StateReason);
        Assert.Equal(SupervisorUserId, control.StateChangedByUserId);
        Assert.Equal(InspectedAtUtc, control.StateChangedAtUtc);
        Assert.Equal(ContainerControlAction.Held, entry.Action);
        Assert.Equal("Damaged corner post", entry.Notes);
    }

    [Fact]
    public void Hold_removes_release_eligibility_immediately()
    {
        var control = Released();

        control.Hold("Damaged corner post", SupervisorUserId, ReleasedAtUtc);

        Assert.Equal(ContainerControlState.Held, control.State);
        Assert.False(ContainerControl.AllowsLoading(control.State, false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hold_is_rejected_without_a_reason(string? reason)
    {
        var control = Sealed();

        Assert.Throws<ContainerHoldReasonRequiredException>(
            () => control.Hold(reason!, SupervisorUserId, InspectedAtUtc));

        Assert.Equal(ContainerControlState.Pending, control.State);
    }

    [Fact]
    public void Hold_is_rejected_when_the_container_is_already_held()
    {
        var control = Sealed();
        control.Hold("Damaged corner post", SupervisorUserId, InspectedAtUtc);

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Hold("Still damaged", SupervisorUserId, ReleasedAtUtc));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.AlreadyHeldReason,
            exception.Reason);
    }

    [Theory]
    [InlineData(nameof(ContainerControlState.Pending), false)]
    [InlineData(nameof(ContainerControlState.Held), false)]
    [InlineData(nameof(ContainerControlState.Released), true)]
    public void AllowsLoading_only_accepts_a_released_container(
        string stateName,
        bool expected)
    {
        var state = Enum.Parse<ContainerControlState>(stateName);

        Assert.Equal(expected, ContainerControl.AllowsLoading(state, false));
    }

    [Fact]
    public void AllowsLoading_rejects_a_container_without_a_control_record()
    {
        Assert.False(ContainerControl.AllowsLoading(null, false));
    }

    [Fact]
    public void Every_transition_reports_the_control_it_belongs_to()
    {
        var control = ContainerControl.Start(ContainerId);

        var sealEntry = control.AssignSeal("ML458821", InspectorUserId, SealedAtUtc);
        var inspectionEntry = control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Sound",
            null,
            isReefer: false,
            InspectorUserId,
            InspectedAtUtc);
        var releaseEntry = control.Release(SupervisorUserId, ReleasedAtUtc, null, false);
        var holdEntry = control.Hold("Damaged corner post", SupervisorUserId, ReleasedAtUtc);

        ContainerControlEvent[] entries =
            [sealEntry, .. inspectionEntry, releaseEntry, holdEntry];

        Assert.All(entries, entry => Assert.Equal(control.Id, entry.ContainerControlId));
        Assert.All(entries, entry => Assert.NotEqual(Guid.Empty, entry.Id));
        Assert.Equal(entries.Length, entries.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void Every_transition_rotates_the_concurrency_token()
    {
        var control = ContainerControl.Start(ContainerId);
        var tokens = new List<Guid> { control.ConcurrencyToken };

        control.AssignSeal("ML458821", InspectorUserId, SealedAtUtc);
        tokens.Add(control.ConcurrencyToken);

        control.RecordInspection(
            ContainerInspectionResult.Passed,
            "Sound",
            null,
            isReefer: false,
            InspectorUserId,
            InspectedAtUtc);
        tokens.Add(control.ConcurrencyToken);

        control.Release(SupervisorUserId, ReleasedAtUtc, null, false);
        tokens.Add(control.ConcurrencyToken);

        control.Hold("Damaged corner post", SupervisorUserId, ReleasedAtUtc);
        tokens.Add(control.ConcurrencyToken);

        Assert.DoesNotContain(Guid.Empty, tokens);
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void A_rejected_transition_leaves_the_concurrency_token_untouched()
    {
        var control = Sealed();
        var token = control.ConcurrencyToken;

        Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, false));

        Assert.Equal(token, control.ConcurrencyToken);
    }

    [Fact]
    public void An_open_temperature_excursion_blocks_a_release_ready_container()
    {
        var control = Inspected(ContainerInspectionResult.Passed);

        Assert.False(control.CanRelease(hasOpenTemperatureExcursion: true));
        Assert.Equal(
            ContainerControlTransitionNotAllowedException.OpenTemperatureExcursionReason,
            control.ReleaseBlocker(hasOpenTemperatureExcursion: true));
    }

    [Fact]
    public void An_open_temperature_excursion_is_reported_ahead_of_other_blockers()
    {
        var control = ContainerControl.Start(ContainerId);

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.OpenTemperatureExcursionReason,
            control.ReleaseBlocker(hasOpenTemperatureExcursion: true));
    }

    [Fact]
    public void Release_is_rejected_while_a_temperature_excursion_is_open()
    {
        var control = Inspected(ContainerInspectionResult.Passed);

        var exception = Assert.Throws<ContainerControlTransitionNotAllowedException>(
            () => control.Release(SupervisorUserId, ReleasedAtUtc, null, true));

        Assert.Equal(
            ContainerControlTransitionNotAllowedException.OpenTemperatureExcursionReason,
            exception.Reason);
        Assert.Equal(ContainerControlState.Pending, control.State);
    }

    [Fact]
    public void An_open_temperature_excursion_blocks_loading_a_released_container()
    {
        var control = Released();

        Assert.Equal(ContainerControlState.Released, control.State);
        Assert.True(ContainerControl.AllowsLoading(control.State, false));
        Assert.False(ContainerControl.AllowsLoading(control.State, true));
    }

    private static ContainerControl Sealed()
    {
        var control = ContainerControl.Start(ContainerId);
        control.AssignSeal("ML458821", InspectorUserId, SealedAtUtc);

        return control;
    }

    private static ContainerControl Inspected(ContainerInspectionResult result)
    {
        var control = Sealed();
        control.RecordInspection(
            result,
            "Doors aligned",
            null,
            isReefer: false,
            InspectorUserId,
            InspectedAtUtc);

        return control;
    }

    private static ContainerControl Released()
    {
        var control = Inspected(ContainerInspectionResult.Passed);
        control.Release(SupervisorUserId, ReleasedAtUtc, "Cleared for loading", false);

        return control;
    }
}
