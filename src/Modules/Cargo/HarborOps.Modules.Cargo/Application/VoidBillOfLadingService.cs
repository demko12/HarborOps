using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class VoidBillOfLadingService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<BillOfLadingDetails> VoidAsync(
        Guid billOfLadingId,
        Guid actorUserId,
        VoidBillOfLadingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = BillOfLadingValidator.ValidateVoid(request);
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

        var selections = await BillOfLadingGuard.GetSelectionsAsync(
            dbContext,
            bill.Id,
            cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        BillOfLadingEvent entry;

        try
        {
            entry = bill.Void(request.Reason!, actorUserId, utcNow);
        }
        catch (BillOfLadingTransitionNotAllowedException exception)
        {
            throw new BillOfLadingTransitionException(
                billOfLadingId,
                exception.CurrentStatus,
                exception.RequestedAction,
                exception.Reason);
        }

        foreach (var selection in selections)
        {
            selection.ReleaseSelection();
        }

        var bookingContainers = await BillOfLadingGuard.GetBookingContainersAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        await dbContext.Set<BillOfLadingEvent>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BillOfLadingMapper.ToDetails(bill, booking.Status, selections, bookingContainers);
    }
}
