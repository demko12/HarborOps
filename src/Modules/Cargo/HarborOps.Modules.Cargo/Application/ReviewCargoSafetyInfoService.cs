using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class ReviewCargoSafetyInfoService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CargoSafetyInfoDetails> ReviewAsync(
        Guid cargoBookingId,
        Guid reviewedByUserId,
        ReviewCargoSafetyInfoRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = CargoSafetyInfoValidator.ValidateReview(request);
        if (errors.Count > 0)
        {
            throw new CargoSafetyInfoValidationException(errors);
        }

        await CargoSafetyInfoGuard.EnsureHazardousAsync(dbContext, cargoBookingId, cancellationToken);

        var declaration = await CargoSafetyInfoGuard.FindDeclarationAsync(
            dbContext,
            cargoBookingId,
            cancellationToken);

        CargoSafetyInfoValidator.TryParseDecision(request.Status, out var decision);

        var reviewedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        CargoSafetyReviewEntry entry;

        try
        {
            entry = decision is CargoSafetyReviewStatus.Approved
                ? declaration.Approve(reviewedByUserId, reviewedAtUtc, request.Notes)
                : declaration.Reject(reviewedByUserId, reviewedAtUtc, request.Notes);
        }
        catch (CargoSafetyInfoTransitionNotAllowedException exception)
        {
            throw new CargoSafetyInfoTransitionException(
                exception.CargoSafetyInfoId,
                exception.CurrentStatus,
                exception.RequestedAction);
        }
        catch (CargoSafetyReviewNotesRequiredException)
        {
            throw new CargoSafetyInfoValidationException(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [CargoSafetyInfoValidator.NotesField] =
                        ["Review notes are required when a declaration is rejected."],
                });
        }

        await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(entry, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CargoSafetyInfoMapper.ToDetails(declaration);
    }
}
