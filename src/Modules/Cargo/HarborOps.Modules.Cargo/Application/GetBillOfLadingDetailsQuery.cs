using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetBillOfLadingDetailsQuery(DbContext dbContext)
{
    public async Task<GetBillOfLadingResponse> GetAsync(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        var bill = await dbContext
            .Set<BillOfLading>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == billOfLadingId, cancellationToken)
            ?? throw new BillOfLadingNotFoundException(billOfLadingId);

        var booking = await BillOfLadingGuard.FindBookingAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        var selections = await dbContext
            .Set<BillOfLadingContainer>()
            .AsNoTracking()
            .Where(selection => selection.BillOfLadingId == bill.Id)
            .ToListAsync(cancellationToken);

        var bookingContainers = await BillOfLadingGuard.GetBookingContainersAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        var history = await dbContext
            .Set<BillOfLadingEvent>()
            .AsNoTracking()
            .Where(entry => entry.BillOfLadingId == bill.Id)
            .OrderBy(entry => entry.OccurredUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new BillOfLadingEventListItem(
                entry.Id,
                entry.Action.ToString(),
                entry.FromStatus.ToString(),
                entry.ToStatus.ToString(),
                entry.Notes,
                entry.ActorUserId,
                entry.OccurredUtc))
            .ToListAsync(cancellationToken);

        var details = BillOfLadingMapper.ToDetails(
            bill,
            booking.Status,
            selections,
            bookingContainers);

        return new GetBillOfLadingResponse(details, history);
    }
}
