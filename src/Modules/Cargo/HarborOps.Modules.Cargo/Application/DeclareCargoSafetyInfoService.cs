using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class DeclareCargoSafetyInfoService(DbContext dbContext)
{
    public async Task<DeclareCargoSafetyInfoResponse> DeclareAsync(
        Guid cargoBookingId,
        DeclareCargoSafetyInfoRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = DeclareCargoSafetyInfoValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new DeclareCargoSafetyInfoValidationException(errors);
        }

        var bookings = dbContext.Set<CargoBooking>();
        var safetyInfos = dbContext.Set<CargoSafetyInfo>();

        var booking = await bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == cargoBookingId,
                cancellationToken);

        if (booking is null)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        if (!booking.IsHazardous)
        {
            throw new CargoBookingNotHazardousException(cargoBookingId);
        }

        var alreadyDeclared = await safetyInfos
            .AsNoTracking()
            .AnyAsync(
                safetyInfo => safetyInfo.CargoBookingId == cargoBookingId,
                cancellationToken);

        if (alreadyDeclared)
        {
            throw new CargoSafetyInfoAlreadyDeclaredException(cargoBookingId);
        }

        var declaration = CargoSafetyInfo.Declare(
            cargoBookingId,
            request.ImdgClass!,
            request.UnNumber!,
            request.StorageConditions!,
            request.HandlingNotes);

        await safetyInfos.AddAsync(declaration, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DeclareCargoSafetyInfoResponse(declaration.Id);
    }
}
