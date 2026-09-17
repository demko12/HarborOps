using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class IssueBillOfLadingService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<BillOfLadingDetails> IssueAsync(
        Guid billOfLadingId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var bill = await BillOfLadingGuard.FindBillAsync(
            dbContext,
            billOfLadingId,
            cancellationToken);

        var booking = await BillOfLadingGuard.FindBookingAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        var selections = await BillOfLadingGuard.GetSelectionsAsync(
            dbContext,
            bill.Id,
            cancellationToken);

        var bookingContainers = await BillOfLadingGuard.GetBookingContainersAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        if (bill.Status is BillOfLadingStatus.Draft)
        {
            var selectedContainerIds = selections
                .Select(selection => selection.ContainerId)
                .ToList();

            BillOfLadingGuard.EnsureContainersBelongToBooking(
                bill.CargoBookingId,
                bookingContainers,
                selectedContainerIds);

            await BillOfLadingGuard.EnsureContainersAreFreeAsync(
                dbContext,
                bill.Id,
                bookingContainers,
                selectedContainerIds,
                cancellationToken);
        }

        var snapshot = new BillOfLadingBookingSnapshot(
            booking.BookingNumber,
            booking.VoyageId,
            booking.CargoType,
            booking.Description,
            booking.PackageCount,
            booking.WeightKg,
            booking.Status);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        BillOfLadingEvent entry;

        try
        {
            entry = bill.Issue(snapshot, selections.Count, actorUserId, utcNow);
        }
        catch (BillOfLadingTransitionNotAllowedException exception)
        {
            throw new BillOfLadingTransitionException(
                billOfLadingId,
                exception.CurrentStatus,
                exception.RequestedAction,
                exception.Reason);
        }

        CaptureContainerSnapshots(selections, bookingContainers);

        await dbContext.Set<BillOfLadingEvent>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BillOfLadingMapper.ToDetails(bill, booking.Status, selections, bookingContainers);
    }

    private static void CaptureContainerSnapshots(
        IReadOnlyList<BillOfLadingContainer> selections,
        IReadOnlyList<Container> bookingContainers)
    {
        foreach (var selection in selections)
        {
            var container = bookingContainers
                .Single(candidate => candidate.Id == selection.ContainerId);

            selection.CaptureSnapshot(container.ContainerNumber, container.ContainerType);
        }
    }
}
