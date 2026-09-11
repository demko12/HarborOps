using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class BillOfLadingTests
{
    private static readonly Guid CargoBookingId =
        Guid.Parse("8a7b6c5d-4e3f-4a2b-9c8d-7e6f5a4b3c2d");

    private static readonly Guid VoyageId =
        Guid.Parse("1b2c3d4e-5f60-4718-9a2b-3c4d5e6f7081");

    private static readonly Guid OfficerUserId =
        Guid.Parse("9f8e7d6c-5b4a-4392-8172-6a5b4c3d2e1f");

    private static readonly Guid SupervisorUserId =
        Guid.Parse("4d3c2b1a-0f9e-4d8c-8b7a-695847362514");

    private static readonly DateTime DraftedAtUtc =
        new(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime IssuedAtUtc =
        new(2026, 9, 8, 10, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime VoidedAtUtc =
        new(2026, 9, 8, 14, 45, 0, DateTimeKind.Utc);

    [Fact]
    public void Draft_starts_in_the_draft_status_without_issue_or_void_data()
    {
        var bill = Drafted();

        Assert.Equal(CargoBookingId, bill.CargoBookingId);
        Assert.Equal(BillOfLadingStatus.Draft, bill.Status);
        Assert.True(bill.IsEditable);
        Assert.Null(bill.IssuedByUserId);
        Assert.Null(bill.IssuedAtUtc);
        Assert.Null(bill.VoidedByUserId);
        Assert.Null(bill.VoidReason);
        Assert.Null(bill.SnapshotBookingNumber);
        Assert.Equal(DraftedAtUtc, bill.CreatedUtc);
    }

    [Fact]
    public void Draft_normalizes_the_bill_number()
    {
        var bill = Drafted(Values(billNumber: "  bl 2026 0001 "));

        Assert.Equal("BL20260001", bill.BillNumber);
    }

    [Fact]
    public void NormalizeBillNumber_strips_whitespace_and_upper_cases()
    {
        Assert.Equal("BL-2026-0001", BillOfLading.NormalizeBillNumber(" bl-2026-0001 "));
        Assert.Equal("BL20260001", BillOfLading.NormalizeBillNumber("bl 2026 0001"));
    }

    [Fact]
    public void Draft_trims_party_and_routing_values()
    {
        var bill = Drafted(Values(shipperName: "  Adriatic Trade  "));

        Assert.Equal("Adriatic Trade", bill.ShipperName);
    }

    [Fact]
    public void Draft_keeps_an_absent_notify_party_null()
    {
        var bill = Drafted(Values(notifyPartyName: "   "));

        Assert.Null(bill.NotifyPartyName);
    }

    [Fact]
    public void UpdateDraft_replaces_the_values_and_records_the_actor()
    {
        var bill = Drafted();

        var entry = bill.UpdateDraft(
            Values(consigneeName: "Nordic Imports AB"),
            OfficerUserId,
            IssuedAtUtc);

        Assert.Equal("Nordic Imports AB", bill.ConsigneeName);
        Assert.Equal(BillOfLadingAction.Updated, entry.Action);
        Assert.Equal(BillOfLadingStatus.Draft, entry.FromStatus);
        Assert.Equal(BillOfLadingStatus.Draft, entry.ToStatus);
        Assert.Equal(OfficerUserId, entry.ActorUserId);
        Assert.Equal(IssuedAtUtc, entry.OccurredUtc);
    }

    [Fact]
    public void UpdateDraft_is_rejected_once_the_bill_is_issued()
    {
        var bill = Issued();

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.UpdateDraft(Values(), OfficerUserId, VoidedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.AlreadyIssuedReason,
            exception.Reason);
    }

    [Fact]
    public void UpdateDraft_is_rejected_once_the_bill_is_voided()
    {
        var bill = Issued();
        bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc);

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.UpdateDraft(Values(), OfficerUserId, VoidedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.AlreadyVoidedReason,
            exception.Reason);
    }

    [Fact]
    public void Issue_records_the_issuer_the_timestamp_and_the_booking_snapshot()
    {
        var bill = Drafted();

        var entry = bill.Issue(Snapshot(), 2, OfficerUserId, IssuedAtUtc);

        Assert.Equal(BillOfLadingStatus.Issued, bill.Status);
        Assert.False(bill.IsEditable);
        Assert.Equal(OfficerUserId, bill.IssuedByUserId);
        Assert.Equal(IssuedAtUtc, bill.IssuedAtUtc);
        Assert.Equal("BK-2026-0001", bill.SnapshotBookingNumber);
        Assert.Equal(VoyageId, bill.SnapshotVoyageId);
        Assert.Equal("General cargo", bill.SnapshotCargoType);
        Assert.Equal(480, bill.SnapshotBookingPackageCount);
        Assert.Equal(12400m, bill.SnapshotBookingWeightKg);
        Assert.Equal(CargoStatus.Loaded, bill.SnapshotBookingStatus);
        Assert.Equal(BillOfLadingAction.Issued, entry.Action);
        Assert.Equal(BillOfLadingStatus.Draft, entry.FromStatus);
        Assert.Equal(BillOfLadingStatus.Issued, entry.ToStatus);
    }

    [Fact]
    public void Issue_is_rejected_without_a_selected_container()
    {
        var bill = Drafted();

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.Issue(Snapshot(), 0, OfficerUserId, IssuedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.NoContainersReason,
            exception.Reason);
    }

    [Theory]
    [InlineData(nameof(CargoStatus.Registered))]
    [InlineData(nameof(CargoStatus.Planned))]
    public void Issue_is_rejected_before_the_booking_is_loaded(string statusName)
    {
        var bill = Drafted();
        var status = Enum.Parse<CargoStatus>(statusName);

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.Issue(Snapshot(status), 1, OfficerUserId, IssuedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.BookingNotLoadedReason,
            exception.Reason);
    }

    [Theory]
    [InlineData(nameof(CargoStatus.Loaded))]
    [InlineData(nameof(CargoStatus.InTransit))]
    [InlineData(nameof(CargoStatus.Arrived))]
    [InlineData(nameof(CargoStatus.Unloaded))]
    [InlineData(nameof(CargoStatus.Completed))]
    public void Issue_is_allowed_from_the_loaded_status_onwards(string statusName)
    {
        var bill = Drafted();
        var status = Enum.Parse<CargoStatus>(statusName);

        bill.Issue(Snapshot(status), 1, OfficerUserId, IssuedAtUtc);

        Assert.Equal(BillOfLadingStatus.Issued, bill.Status);
        Assert.Equal(status, bill.SnapshotBookingStatus);
    }

    [Fact]
    public void Issue_is_rejected_when_a_required_detail_is_missing()
    {
        var bill = Drafted(Values(shipperName: "   "));

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.Issue(Snapshot(), 1, OfficerUserId, IssuedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.IncompleteReason,
            exception.Reason);
    }

    [Fact]
    public void Issue_is_rejected_when_the_bill_is_already_issued()
    {
        var bill = Issued();

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.Issue(Snapshot(), 1, OfficerUserId, VoidedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.AlreadyIssuedReason,
            exception.Reason);
    }

    [Fact]
    public void Issue_is_rejected_when_the_bill_is_voided()
    {
        var bill = Issued();
        bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc);

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.Issue(Snapshot(), 1, OfficerUserId, VoidedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.AlreadyVoidedReason,
            exception.Reason);
    }

    [Fact]
    public void IssueBlocker_reports_nothing_while_the_bill_is_ready()
    {
        var bill = Drafted();

        Assert.Null(bill.IssueBlocker(CargoStatus.Loaded, 1));
    }

    [Fact]
    public void Void_records_the_reason_the_actor_and_the_timestamp()
    {
        var bill = Issued();

        var entry = bill.Void("  Wrong consignee  ", SupervisorUserId, VoidedAtUtc);

        Assert.Equal(BillOfLadingStatus.Voided, bill.Status);
        Assert.Equal("Wrong consignee", bill.VoidReason);
        Assert.Equal(SupervisorUserId, bill.VoidedByUserId);
        Assert.Equal(VoidedAtUtc, bill.VoidedAtUtc);
        Assert.Equal(BillOfLadingAction.Voided, entry.Action);
        Assert.Equal(BillOfLadingStatus.Issued, entry.FromStatus);
        Assert.Equal(BillOfLadingStatus.Voided, entry.ToStatus);
        Assert.Equal("Wrong consignee", entry.Notes);
    }

    [Fact]
    public void Void_keeps_the_issued_snapshot_intact()
    {
        var bill = Issued();

        bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc);

        Assert.Equal("BK-2026-0001", bill.SnapshotBookingNumber);
        Assert.Equal(VoyageId, bill.SnapshotVoyageId);
        Assert.Equal(480, bill.SnapshotBookingPackageCount);
        Assert.Equal(OfficerUserId, bill.IssuedByUserId);
        Assert.Equal(IssuedAtUtc, bill.IssuedAtUtc);
    }

    [Fact]
    public void Void_is_rejected_while_the_bill_is_still_a_draft()
    {
        var bill = Drafted();

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.StillDraftReason,
            exception.Reason);
    }

    [Fact]
    public void Void_is_rejected_when_the_bill_is_already_voided()
    {
        var bill = Issued();
        bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc);

        var exception = Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.Void("Another reason", SupervisorUserId, VoidedAtUtc));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.AlreadyVoidedReason,
            exception.Reason);
    }

    [Fact]
    public void Void_is_rejected_without_a_reason()
    {
        var bill = Issued();

        Assert.Throws<BillOfLadingVoidReasonRequiredException>(
            () => bill.Void("   ", SupervisorUserId, VoidedAtUtc));

        Assert.Equal(BillOfLadingStatus.Issued, bill.Status);
    }

    [Fact]
    public void A_voided_bill_stops_blocking_its_containers()
    {
        var bill = Issued();

        Assert.True(bill.BlocksContainerSelection);

        bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc);

        Assert.False(bill.BlocksContainerSelection);
    }

    [Fact]
    public void A_draft_blocks_its_containers_just_like_an_issued_bill()
    {
        Assert.True(Drafted().BlocksContainerSelection);
    }

    [Fact]
    public void Every_transition_rotates_the_concurrency_token()
    {
        var bill = Drafted();
        var afterDraft = bill.ConcurrencyToken;

        bill.UpdateDraft(Values(), OfficerUserId, DraftedAtUtc);
        var afterUpdate = bill.ConcurrencyToken;

        bill.Issue(Snapshot(), 1, OfficerUserId, IssuedAtUtc);
        var afterIssue = bill.ConcurrencyToken;

        bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc);

        Assert.NotEqual(afterDraft, afterUpdate);
        Assert.NotEqual(afterUpdate, afterIssue);
        Assert.NotEqual(afterIssue, bill.ConcurrencyToken);
    }

    [Fact]
    public void A_rejected_transition_leaves_the_concurrency_token_untouched()
    {
        var bill = Issued();
        var token = bill.ConcurrencyToken;

        Assert.Throws<BillOfLadingTransitionNotAllowedException>(
            () => bill.UpdateDraft(Values(), OfficerUserId, VoidedAtUtc));

        Assert.Equal(token, bill.ConcurrencyToken);
    }

    [Fact]
    public void Every_event_names_the_bill_it_belongs_to()
    {
        var bill = Drafted();

        var updated = bill.UpdateDraft(Values(), OfficerUserId, DraftedAtUtc);
        var issued = bill.Issue(Snapshot(), 1, OfficerUserId, IssuedAtUtc);
        var voided = bill.Void("Wrong consignee", SupervisorUserId, VoidedAtUtc);

        Assert.Equal(bill.Id, updated.BillOfLadingId);
        Assert.Equal(bill.Id, issued.BillOfLadingId);
        Assert.Equal(bill.Id, voided.BillOfLadingId);
    }

    private static BillOfLading Drafted(BillOfLadingDraftValues? values = null)
    {
        return BillOfLading.Draft(CargoBookingId, values ?? Values(), DraftedAtUtc);
    }

    private static BillOfLading Issued()
    {
        var bill = Drafted();
        bill.Issue(Snapshot(), 2, OfficerUserId, IssuedAtUtc);

        return bill;
    }

    private static BillOfLadingBookingSnapshot Snapshot(
        CargoStatus status = CargoStatus.Loaded)
    {
        return new BillOfLadingBookingSnapshot(
            "BK-2026-0001",
            VoyageId,
            "General cargo",
            "Machine parts",
            480,
            12400m,
            status);
    }

    private static BillOfLadingDraftValues Values(
        string billNumber = "BL-2026-0001",
        string shipperName = "Adriatic Trade",
        string consigneeName = "Nordic Imports",
        string? notifyPartyName = "Port Agency")
    {
        return new BillOfLadingDraftValues(
            billNumber,
            shipperName,
            "Bulevar Revolucije 1, Bar",
            consigneeName,
            "Hamngatan 4, Goteborg",
            notifyPartyName,
            "Obala 2, Bar",
            "Bar",
            "Rotterdam",
            "Bar",
            "Podgorica",
            FreightTerms.Prepaid,
            "ADT/2026/0001",
            "Machine parts, palletised",
            480,
            12400m,
            "Bar",
            new DateOnly(2026, 9, 8));
    }
}
