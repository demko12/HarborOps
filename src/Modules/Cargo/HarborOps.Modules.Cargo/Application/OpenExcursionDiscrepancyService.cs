using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class OpenExcursionDiscrepancyService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<OpenExcursionDiscrepancyResponse> OpenAsync(
        Guid containerId,
        Guid excursionId,
        CargoDiscrepancyActor reporter,
        OpenExcursionDiscrepancyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        ArgumentNullException.ThrowIfNull(request);

        var container = await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var excursion = await dbContext
            .Set<ReeferExcursion>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == excursionId && candidate.ContainerId == containerId,
                cancellationToken)
            ?? throw new ReeferExcursionNotFoundException(excursionId);

        if (await FindExistingAsync(excursion.Id, cancellationToken) is { } existing)
        {
            return await OpenedAsync(existing, created: false, cancellationToken);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var errors = CargoDiscrepancyValidator.ValidateExcursionRegistration(request, utcNow);
        if (errors.Count > 0)
        {
            throw new CargoDiscrepancyValidationException(errors);
        }

        if (container.CargoBookingId is not { } cargoBookingId)
        {
            throw new ContainerHasNoBookingException(containerId);
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

        CargoDiscrepancyValidator.TryParse<CargoDiscrepancySeverity>(request.Severity, out var severity);

        var observedAtUtc = request.ObservedAtUtc is { } observedAt
            ? CargoDiscrepancyValidator.NormalizeToUtc(observedAt)
            : excursion.MeasuredAtUtc;

        var (discrepancy, reported) = CargoDiscrepancy.Register(
            booking,
            container,
            excursion,
            CargoDiscrepancyType.TemperatureRelated,
            severity,
            request.Description!,
            observedAtUtc,
            reporter.UserId,
            reporter.Name,
            utcNow);

        booking.Touch();

        await dbContext.Set<CargoDiscrepancy>().AddAsync(discrepancy, cancellationToken);
        await dbContext.Set<CargoDiscrepancyEvent>().AddAsync(reported, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();

            if (await FindExistingAsync(excursion.Id, cancellationToken) is { } winner)
            {
                return await OpenedAsync(winner, created: false, cancellationToken);
            }

            throw;
        }

        return await OpenedAsync(discrepancy, created: true, cancellationToken);
    }

    private Task<CargoDiscrepancy?> FindExistingAsync(Guid excursionId, CancellationToken cancellationToken)
    {
        return dbContext
            .Set<CargoDiscrepancy>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                discrepancy => discrepancy.SourceExcursionId == excursionId,
                cancellationToken);
    }

    private async Task<OpenExcursionDiscrepancyResponse> OpenedAsync(
        CargoDiscrepancy discrepancy,
        bool created,
        CancellationToken cancellationToken)
    {
        var details = await GetCargoDiscrepanciesQuery.LoadDetailsAsync(dbContext, discrepancy, cancellationToken);

        return new OpenExcursionDiscrepancyResponse(details, created);
    }
}
