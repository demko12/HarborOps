using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class UpdateBillOfLadingDraftService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<BillOfLadingDetails> UpdateAsync(
        Guid billOfLadingId,
        Guid actorUserId,
        SaveBillOfLadingDraftRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = BillOfLadingValidator.ValidateDraft(request);
        if (errors.Count > 0)
        {
            throw new BillOfLadingValidationException(errors);
        }

        var bill = await BillOfLadingGuard.FindBillAsync(
            dbContext,
            billOfLadingId,
            cancellationToken);

        var booking = await BillOfLadingGuard.FindBookingAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        BillOfLadingGuard.EnsureTotalsFitTheBooking(request, booking);

        var values = BillOfLadingGuard.ToDraftValues(request);
        var billNumber = BillOfLading.NormalizeBillNumber(values.BillNumber);

        await BillOfLadingGuard.EnsureBillNumberIsFreeAsync(
            dbContext,
            bill.Id,
            billNumber,
            cancellationToken);

        var requestedContainerIds = request.ContainerIds ?? [];
        var bookingContainers = await BillOfLadingGuard.GetBookingContainersAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        BillOfLadingGuard.EnsureContainersBelongToBooking(
            bill.CargoBookingId,
            bookingContainers,
            requestedContainerIds);

        await BillOfLadingGuard.EnsureContainersAreFreeAsync(
            dbContext,
            bill.Id,
            bookingContainers,
            requestedContainerIds,
            cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        BillOfLadingEvent entry;

        try
        {
            entry = bill.UpdateDraft(values, actorUserId, utcNow);
        }
        catch (BillOfLadingTransitionNotAllowedException exception)
        {
            throw new BillOfLadingTransitionException(
                billOfLadingId,
                exception.CurrentStatus,
                exception.RequestedAction,
                exception.Reason);
        }

        var selections = await SyncSelectionsAsync(
            bill.Id,
            requestedContainerIds,
            cancellationToken);

        await dbContext.Set<BillOfLadingEvent>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BillOfLadingMapper.ToDetails(bill, booking.Status, selections, bookingContainers);
    }

    private async Task<IReadOnlyList<BillOfLadingContainer>> SyncSelectionsAsync(
        Guid billOfLadingId,
        IReadOnlyList<Guid> requestedContainerIds,
        CancellationToken cancellationToken)
    {
        var existing = await BillOfLadingGuard.GetSelectionsAsync(
            dbContext,
            billOfLadingId,
            cancellationToken);

        var removed = existing
            .Where(selection => !requestedContainerIds.Contains(selection.ContainerId))
            .ToList();

        dbContext.Set<BillOfLadingContainer>().RemoveRange(removed);

        var kept = existing.Except(removed).ToList();
        var keptContainerIds = kept.Select(selection => selection.ContainerId).ToHashSet();

        var added = requestedContainerIds
            .Where(containerId => !keptContainerIds.Contains(containerId))
            .Select(containerId => BillOfLadingContainer.Select(billOfLadingId, containerId))
            .ToList();

        await dbContext.Set<BillOfLadingContainer>().AddRangeAsync(added, cancellationToken);

        return [.. kept, .. added];
    }
}
