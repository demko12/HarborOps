using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetBillsOfLadingQuery(DbContext dbContext)
{
    public async Task<GetBillsOfLadingResponse> GetAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var booking = await BillOfLadingGuard.FindBookingAsync(
            dbContext,
            cargoBookingId,
            cancellationToken);

        var bills = await dbContext
            .Set<BillOfLading>()
            .AsNoTracking()
            .Where(bill => bill.CargoBookingId == cargoBookingId)
            .OrderByDescending(bill => bill.CreatedUtc)
            .ThenBy(bill => bill.BillNumber)
            .Select(bill => new BillOfLadingListItem(
                bill.Id,
                bill.BillNumber,
                bill.Status.ToString(),
                bill.ShipperName,
                bill.ConsigneeName,
                bill.PortOfLoading,
                bill.PortOfDischarge,
                dbContext.Set<BillOfLadingContainer>()
                    .Count(selection => selection.BillOfLadingId == bill.Id),
                bill.IssuedByUserId,
                bill.IssuedAtUtc,
                bill.VoidedAtUtc,
                bill.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new GetBillsOfLadingResponse(
            booking.Id,
            booking.BookingNumber,
            booking.Status.ToString(),
            bills);
    }
}
