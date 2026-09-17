using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetBillOfLadingPrintQuery(DbContext dbContext)
{
    public async Task<BillOfLadingPrintModel> GetAsync(
        Guid billOfLadingId,
        CancellationToken cancellationToken)
    {
        var bill = await dbContext
            .Set<BillOfLading>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == billOfLadingId, cancellationToken)
            ?? throw new BillOfLadingNotFoundException(billOfLadingId);

        var selections = await dbContext
            .Set<BillOfLadingContainer>()
            .AsNoTracking()
            .Where(selection => selection.BillOfLadingId == bill.Id)
            .ToListAsync(cancellationToken);

        var bookingContainers = await BillOfLadingGuard.GetBookingContainersAsync(
            dbContext,
            bill.CargoBookingId,
            cancellationToken);

        return BillOfLadingMapper.ToPrintModel(bill, selections, bookingContainers);
    }
}
