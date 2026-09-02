using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class SubmitCargoSafetyInfoService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CargoSafetyInfoDetails> SubmitAsync(
        Guid cargoBookingId,
        Guid submittedByUserId,
        CancellationToken cancellationToken)
    {
        await CargoSafetyInfoGuard.EnsureHazardousAsync(dbContext, cargoBookingId, cancellationToken);

        var declaration = await CargoSafetyInfoGuard.FindDeclarationAsync(
            dbContext,
            cargoBookingId,
            cancellationToken);

        CargoSafetyReviewEntry entry;

        try
        {
            entry = declaration.Submit(submittedByUserId, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (CargoSafetyInfoTransitionNotAllowedException exception)
        {
            throw new CargoSafetyInfoTransitionException(
                exception.CargoSafetyInfoId,
                exception.CurrentStatus,
                exception.RequestedAction);
        }

        await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CargoSafetyInfoMapper.ToDetails(declaration);
    }
}
