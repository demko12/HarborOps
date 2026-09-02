using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetCargoSafetyInformationQuery(DbContext dbContext)
{
    public async Task<GetCargoSafetyInformationResponse> GetAsync(
        Guid cargoBookingId,
        CancellationToken cancellationToken)
    {
        await CargoSafetyInfoGuard.EnsureHazardousAsync(dbContext, cargoBookingId, cancellationToken);

        var declaration = await dbContext
            .Set<CargoSafetyInfo>()
            .AsNoTracking()
            .Where(safetyInfo => safetyInfo.CargoBookingId == cargoBookingId)
            .Select(safetyInfo => new CargoSafetyInfoDetails(
                safetyInfo.Id,
                safetyInfo.ImdgClass,
                safetyInfo.UnNumber,
                safetyInfo.StorageConditions,
                safetyInfo.HandlingNotes,
                safetyInfo.ReviewStatus.ToString(),
                safetyInfo.SubmittedByUserId,
                safetyInfo.SubmittedAtUtc,
                safetyInfo.ReviewedByUserId,
                safetyInfo.ReviewedAtUtc,
                safetyInfo.ReviewNotes))
            .SingleOrDefaultAsync(cancellationToken);

        if (declaration is null)
        {
            throw new CargoSafetyInfoNotFoundException(cargoBookingId);
        }

        var timeline = await dbContext
            .Set<CargoSafetyReviewEntry>()
            .AsNoTracking()
            .Where(entry => entry.CargoSafetyInfoId == declaration.CargoSafetyInfoId)
            .OrderBy(entry => entry.OccurredUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new CargoSafetyReviewEntryListItem(
                entry.Id,
                entry.FromStatus.ToString(),
                entry.ToStatus.ToString(),
                entry.ActorUserId,
                entry.OccurredUtc,
                entry.Notes))
            .ToListAsync(cancellationToken);

        return new GetCargoSafetyInformationResponse(declaration, timeline);
    }
}
