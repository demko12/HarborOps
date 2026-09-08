using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class ExecuteCargoOperationService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CargoOperationTransitionResponse> StartAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await FindOperationAsync(operationId, cancellationToken);

        if (operation.State is CargoOperationState.Planned)
        {
            await EnsureBookingPhaseAllowsAsync(operation, cancellationToken);
            await EnsureHazardousCargoIsApprovedAsync(operation, cancellationToken);
            await EnsureNoOtherOperationInProgressAsync(operation, cancellationToken);
            await EnsureManifestIsReadyAsync(operation, cancellationToken);
        }

        try
        {
            operation.Start(timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (LoadUnloadOperationTransitionNotAllowedException exception)
        {
            throw new CargoOperationTransitionException(
                exception.OperationId,
                exception.CurrentState,
                exception.RequestedAction);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(operation);
    }

    public async Task<CargoOperationTransitionResponse> CompleteAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await FindOperationAsync(operationId, cancellationToken);

        try
        {
            operation.Complete(timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (LoadUnloadOperationTransitionNotAllowedException exception)
        {
            throw new CargoOperationTransitionException(
                exception.OperationId,
                exception.CurrentState,
                exception.RequestedAction);
        }

        var containers = await dbContext
            .Set<Container>()
            .Where(container => container.CargoBookingId == operation.CargoBookingId)
            .ToListAsync(cancellationToken);

        foreach (var container in containers)
        {
            container.ApplyOperationOutcome(operation.OperationType);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(operation);
    }

    private async Task EnsureBookingPhaseAllowsAsync(
        LoadUnloadOperation operation,
        CancellationToken cancellationToken)
    {
        var requiredStatus = LoadUnloadOperation.RequiredBookingStatusFor(operation.OperationType);

        var bookingStatus = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .Where(booking => booking.Id == operation.CargoBookingId)
            .Select(booking => booking.Status)
            .SingleAsync(cancellationToken);

        if (bookingStatus != requiredStatus)
        {
            throw new CargoOperationBookingPhaseException(
                operation.CargoBookingId,
                operation.OperationType,
                bookingStatus,
                requiredStatus);
        }
    }

    private async Task EnsureNoOtherOperationInProgressAsync(
        LoadUnloadOperation operation,
        CancellationToken cancellationToken)
    {
        var hasOperationInProgress = await dbContext
            .Set<LoadUnloadOperation>()
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.CargoBookingId == operation.CargoBookingId
                    && candidate.Id != operation.Id
                    && candidate.StartedAtUtc != null
                    && candidate.FinishedAtUtc == null,
                cancellationToken);

        if (hasOperationInProgress)
        {
            throw new CargoOperationAlreadyInProgressException(operation.CargoBookingId);
        }
    }

    private async Task EnsureHazardousCargoIsApprovedAsync(
        LoadUnloadOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType is not CargoOperationType.Load)
        {
            return;
        }

        var isHazardous = await dbContext
            .Set<CargoBooking>()
            .AsNoTracking()
            .Where(booking => booking.Id == operation.CargoBookingId)
            .Select(booking => booking.IsHazardous)
            .SingleAsync(cancellationToken);

        if (!isHazardous)
        {
            return;
        }

        await CargoSafetyInfoGuard.EnsureApprovedForLoadingAsync(
            dbContext,
            operation.CargoBookingId,
            cancellationToken);
    }

    private async Task EnsureManifestIsReadyAsync(
        LoadUnloadOperation operation,
        CancellationToken cancellationToken)
    {
        var requiredStatus = LoadUnloadOperation.RequiredContainerStatusFor(operation.OperationType);

        var containerStatuses = await dbContext
            .Set<Container>()
            .AsNoTracking()
            .Where(container => container.CargoBookingId == operation.CargoBookingId)
            .Select(container => container.Status)
            .ToListAsync(cancellationToken);

        if (containerStatuses.Count == 0)
        {
            throw new CargoManifestEmptyException(operation.CargoBookingId);
        }

        if (containerStatuses.Exists(status => status != requiredStatus))
        {
            throw new CargoManifestNotReadyException(
                operation.CargoBookingId,
                operation.OperationType,
                requiredStatus);
        }
    }

    private async Task<LoadUnloadOperation> FindOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext
            .Set<LoadUnloadOperation>()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == operationId,
                cancellationToken);

        return operation ?? throw new CargoOperationNotFoundException(operationId);
    }

    private static CargoOperationTransitionResponse ToResponse(LoadUnloadOperation operation)
    {
        return new CargoOperationTransitionResponse(
            operation.Id,
            operation.State.ToString(),
            operation.StartedAtUtc,
            operation.FinishedAtUtc);
    }
}
