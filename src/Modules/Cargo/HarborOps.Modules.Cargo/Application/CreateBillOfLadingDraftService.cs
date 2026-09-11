using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class CreateBillOfLadingDraftService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<BillOfLadingDetails> CreateAsync(
        Guid cargoBookingId,
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

        var booking = await BillOfLadingGuard.FindBookingAsync(
            dbContext,
            cargoBookingId,
            cancellationToken);

        BillOfLadingGuard.EnsureTotalsFitTheBooking(request, booking);

        var values = BillOfLadingGuard.ToDraftValues(request);
        var billNumber = BillOfLading.NormalizeBillNumber(values.BillNumber);

        await BillOfLadingGuard.EnsureBillNumberIsFreeAsync(
            dbContext,
            Guid.Empty,
            billNumber,
            cancellationToken);

        var requestedContainerIds = request.ContainerIds ?? [];
        var bookingContainers = await BillOfLadingGuard.GetBookingContainersAsync(
            dbContext,
            cargoBookingId,
            cancellationToken);

        BillOfLadingGuard.EnsureContainersBelongToBooking(
            cargoBookingId,
            bookingContainers,
            requestedContainerIds);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var bill = BillOfLading.Draft(cargoBookingId, values, utcNow);

        await BillOfLadingGuard.EnsureContainersAreFreeAsync(
            dbContext,
            bill.Id,
            bookingContainers,
            requestedContainerIds,
            cancellationToken);

        var selections = requestedContainerIds
            .Select(containerId => BillOfLadingContainer.Select(bill.Id, containerId))
            .ToList();

        var entry = BillOfLadingEvent.Record(
            bill.Id,
            BillOfLadingAction.Drafted,
            BillOfLadingStatus.Draft,
            BillOfLadingStatus.Draft,
            actorUserId,
            utcNow);

        await dbContext.Set<BillOfLading>().AddAsync(bill, cancellationToken);
        await dbContext.Set<BillOfLadingContainer>().AddRangeAsync(selections, cancellationToken);
        await dbContext.Set<BillOfLadingEvent>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BillOfLadingMapper.ToDetails(bill, booking.Status, selections, bookingContainers);
    }
}
