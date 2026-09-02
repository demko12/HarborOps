using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoOperationsQuery(DbContext dbContext)
{
    public async Task<GetCargoOperationsResponse> GetOperationsAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var bookingExists = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .AnyAsync(booking => booking.Id == cargoBookingId, cancellationToken);

        if (!bookingExists)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        var items = await dbContext
            .Set<LoadUnloadOperation>()
            .AsNoTracking()
            .Where(operation => operation.CargoBookingId == cargoBookingId)
            .OrderBy(operation => operation.PlannedAtUtc)
            .ThenBy(operation => operation.Id)
            .Select(operation => new CargoOperationListItem(
                operation.Id,
                operation.OperationType.ToString(),
                operation.FinishedAtUtc != null
                    ? nameof(CargoOperationState.Completed)
                    : operation.StartedAtUtc != null
                        ? nameof(CargoOperationState.InProgress)
                        : nameof(CargoOperationState.Planned),
                operation.PlannedAtUtc,
                operation.StartedAtUtc,
                operation.FinishedAtUtc,
                operation.ResponsibleUserId))
            .ToListAsync(cancellationToken);

        return new GetCargoOperationsResponse(items);
    }
}
