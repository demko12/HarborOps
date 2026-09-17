using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class BillOfLadingMapper
{
    internal static BillOfLadingDetails ToDetails(
        BillOfLading bill,
        CargoStatus bookingStatus,
        IReadOnlyList<BillOfLadingContainer> selections,
        IReadOnlyList<Container> bookingContainers)
    {
        var containers = ToContainerItems(selections, bookingContainers);
        var blocker = bill.IssueBlocker(bookingStatus, containers.Count);

        return new BillOfLadingDetails(
            bill.Id,
            bill.CargoBookingId,
            bill.BillNumber,
            bill.Status.ToString(),
            bill.ShipperName,
            bill.ShipperAddress,
            bill.ConsigneeName,
            bill.ConsigneeAddress,
            bill.NotifyPartyName,
            bill.NotifyPartyAddress,
            bill.PlaceOfReceipt,
            bill.PortOfLoading,
            bill.PortOfDischarge,
            bill.PlaceOfDelivery,
            bill.FreightTerms.ToString(),
            bill.CargoMarks,
            bill.CargoDescription,
            bill.PackageCount,
            bill.WeightKg,
            bill.IssuePlace,
            bill.RequestedIssueDate,
            bill.IssuedByUserId,
            bill.IssuedAtUtc,
            bill.VoidedByUserId,
            bill.VoidedAtUtc,
            bill.VoidReason,
            bill.SnapshotBookingNumber,
            bill.SnapshotVoyageId,
            bill.SnapshotCargoType,
            bill.SnapshotBookingDescription,
            bill.SnapshotBookingPackageCount,
            bill.SnapshotBookingWeightKg,
            bill.SnapshotBookingStatus?.ToString(),
            bill.IsEditable,
            blocker is null,
            blocker,
            bill.CreatedUtc,
            containers);
    }

    internal static IReadOnlyList<BillOfLadingContainerListItem> ToContainerItems(
        IReadOnlyList<BillOfLadingContainer> selections,
        IReadOnlyList<Container> bookingContainers)
    {
        return
        [
            .. selections
                .Select(selection => ToContainerItem(selection, bookingContainers))
                .OrderBy(item => item.ContainerNumber, StringComparer.Ordinal),
        ];
    }

    internal static BillOfLadingPrintModel ToPrintModel(
        BillOfLading bill,
        IReadOnlyList<BillOfLadingContainer> selections,
        IReadOnlyList<Container> bookingContainers)
    {
        return new BillOfLadingPrintModel(
            bill.Id,
            bill.BillNumber,
            bill.Status.ToString(),
            WatermarkFor(bill.Status),
            bill.ShipperName,
            bill.ShipperAddress,
            bill.ConsigneeName,
            bill.ConsigneeAddress,
            bill.NotifyPartyName,
            bill.NotifyPartyAddress,
            bill.PlaceOfReceipt,
            bill.PortOfLoading,
            bill.PortOfDischarge,
            bill.PlaceOfDelivery,
            bill.FreightTerms.ToString(),
            bill.CargoMarks,
            bill.CargoDescription,
            bill.PackageCount,
            bill.WeightKg,
            bill.IssuePlace,
            bill.RequestedIssueDate,
            bill.SnapshotBookingNumber,
            bill.IssuedByUserId,
            bill.IssuedAtUtc,
            bill.VoidedByUserId,
            bill.VoidedAtUtc,
            bill.VoidReason,
            ToContainerItems(selections, bookingContainers));
    }

    internal static string? WatermarkFor(BillOfLadingStatus status)
    {
        return status switch
        {
            BillOfLadingStatus.Draft => nameof(BillOfLadingStatus.Draft),
            BillOfLadingStatus.Voided => nameof(BillOfLadingStatus.Voided),
            _ => null,
        };
    }

    internal static BillOfLadingEventListItem ToEventItem(BillOfLadingEvent entry)
    {
        return new BillOfLadingEventListItem(
            entry.Id,
            entry.Action.ToString(),
            entry.FromStatus.ToString(),
            entry.ToStatus.ToString(),
            entry.Notes,
            entry.ActorUserId,
            entry.OccurredUtc);
    }

    private static BillOfLadingContainerListItem ToContainerItem(
        BillOfLadingContainer selection,
        IReadOnlyList<Container> bookingContainers)
    {
        if (selection.IsSnapshotted)
        {
            return new BillOfLadingContainerListItem(
                selection.ContainerId,
                selection.SnapshotContainerNumber!,
                selection.SnapshotContainerType!,
                true);
        }

        var container = bookingContainers
            .FirstOrDefault(candidate => candidate.Id == selection.ContainerId);

        return new BillOfLadingContainerListItem(
            selection.ContainerId,
            container?.ContainerNumber ?? string.Empty,
            container?.ContainerType ?? string.Empty,
            false);
    }
}
