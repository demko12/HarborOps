using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class UpdateCargoSafetyInfoService(DbContext dbContext)
{
    public async Task<CargoSafetyInfoDetails> UpdateAsync(
        Guid cargoBookingId,
        UpdateCargoSafetyInfoRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CargoSafetyInfoValidator.ValidateUpdate(request);
        if (errors.Count > 0)
        {
            throw new CargoSafetyInfoValidationException(errors);
        }

        await CargoSafetyInfoGuard.EnsureHazardousAsync(dbContext, cargoBookingId, cancellationToken);

        CargoSafetyFormat.TryNormalizeImdgClass(request.ImdgClass, out var imdgClass);
        CargoSafetyFormat.TryNormalizeUnNumber(request.UnNumber, out var unNumber);

        var safetyInfos = dbContext.Set<CargoSafetyInfo>();

        var declaration = await safetyInfos.SingleOrDefaultAsync(
            safetyInfo => safetyInfo.CargoBookingId == cargoBookingId,
            cancellationToken);

        if (declaration is null)
        {
            declaration = CargoSafetyInfo.Declare(
                cargoBookingId,
                imdgClass,
                unNumber,
                request.StorageConditions!,
                request.HandlingNotes);

            await safetyInfos.AddAsync(declaration, cancellationToken);
        }
        else
        {
            try
            {
                declaration.Update(
                    imdgClass,
                    unNumber,
                    request.StorageConditions!,
                    request.HandlingNotes);
            }
            catch (CargoSafetyInfoTransitionNotAllowedException exception)
            {
                throw new CargoSafetyInfoTransitionException(
                    exception.CargoSafetyInfoId,
                    exception.CurrentStatus,
                    exception.RequestedAction);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return CargoSafetyInfoMapper.ToDetails(declaration);
    }
}
