using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

internal static class CargoSafetyInfoGuard
{
    internal static async Task EnsureHazardousAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var isHazardous = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .Where(booking => booking.Id == cargoBookingId)
            .Select(booking => (bool?)booking.IsHazardous)
            .SingleOrDefaultAsync(cancellationToken);

        if (isHazardous is null)
        {
            throw new CargoBookingNotFoundException(cargoBookingId);
        }

        if (isHazardous is false)
        {
            throw new CargoBookingNotHazardousException(cargoBookingId);
        }
    }

    internal static async Task<CargoSafetyInfo> FindDeclarationAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var declaration = await dbContext
            .Set<CargoSafetyInfo>()
            .SingleOrDefaultAsync(
                safetyInfo => safetyInfo.CargoBookingId == cargoBookingId,
                cancellationToken);

        return declaration ?? throw new CargoSafetyInfoNotFoundException(cargoBookingId);
    }

    internal static async Task EnsureApprovedForLoadingAsync(
        DbContext dbContext,
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        var reviewStatus = await dbContext
            .Set<CargoSafetyInfo>()
            .AsNoTracking()
            .Where(safetyInfo => safetyInfo.CargoBookingId == cargoBookingId)
            .Select(safetyInfo => (CargoSafetyReviewStatus?)safetyInfo.ReviewStatus)
            .SingleOrDefaultAsync(cancellationToken);

        if (!CargoSafetyInfo.AllowsLoading(reviewStatus))
        {
            throw new CargoSafetyApprovalRequiredException(cargoBookingId, reviewStatus);
        }
    }
}
