using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class RegisterCargoDiscrepancyService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CargoDiscrepancyDetails> RegisterAsync(
        Guid cargoBookingId,
        CargoDiscrepancyActor reporter,
        RegisterCargoDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        ArgumentNullException.ThrowIfNull(request);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = CargoDiscrepancyValidator.ValidateRegistration(request, utcNow);
        if (errors.Count > 0)
        {
            throw new CargoDiscrepancyValidationException(errors);
        }

        var booking = await CargoDiscrepancyGuard.FindBookingAsync(
            dbContext,
            cargoBookingId,
            tracked: true,
            cancellationToken);

        if (booking.Status == CargoStatus.Completed)
        {
            throw new CargoBookingCompletedException(cargoBookingId);
        }

        Container? container = null;

        if (request.ContainerId is { } containerId)
        {
            container = await dbContext
                .Set<Container>()
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == containerId, cancellationToken)
                ?? throw new CargoDiscrepancyValidationException(CargoDiscrepancyValidator.SingleError(
                    CargoDiscrepancyValidator.ContainerIdField,
                    "The container does not exist."));
        }

        CargoDiscrepancyValidator.TryParse<CargoDiscrepancyType>(request.Type, out var type);
        CargoDiscrepancyValidator.TryParse<CargoDiscrepancySeverity>(request.Severity, out var severity);

        var (discrepancy, reported) = CargoDiscrepancy.Register(
            booking,
            container,
            null,
            type,
            severity,
            request.Description!,
            CargoDiscrepancyValidator.NormalizeToUtc(request.ObservedAtUtc!.Value),
            reporter.UserId,
            reporter.Name,
            utcNow);

        booking.Touch();

        await dbContext.Set<CargoDiscrepancy>().AddAsync(discrepancy, cancellationToken);
        await dbContext.Set<CargoDiscrepancyEvent>().AddAsync(reported, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetCargoDiscrepanciesQuery.LoadDetailsAsync(dbContext, discrepancy, cancellationToken);
    }
}
